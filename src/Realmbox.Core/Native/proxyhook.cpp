/*
 * ProxyHook.dll
 *
 * Hooks ws2_32.dll!connect and ws2_32.dll!WSAConnect inside the target process.
 * Every outbound TCP connection is transparently redirected through a SOCKS5 proxy
 * whose address is passed via a shared memory block created by the launcher.
 *
 * The shared memory name is "EAMProxyConfig_<PID>" where <PID> is the target process ID.
 * It contains a null-terminated EAMProxyConfig struct (see below).
 *
 * Injection flow (done by the C# launcher):
 *   1. Launcher creates shared memory "EAMProxyConfig_<PID>" with proxy settings.
 *   2. Launcher calls CreateRemoteThread(LoadLibraryW, "...\\ProxyHook.dll").
 *   3. DllMain runs, reads config from shared memory, installs IAT hooks.
 *   4. Every subsequent connect() in the game goes through SOCKS5.
 *
 * Hook technique: IAT (Import Address Table) patching.
 *   - Simple, no need for a trampoline or third-party detour library.
 *   - Works for IL2CPP Unity games which import ws2_32 via the standard IAT.
 *   - We walk every loaded module's IAT and replace ws2_32!connect / WSAConnect.
 */

#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <imagehlp.h>
#include <string>
#include <cstring>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "imagehlp.lib")

// ------------------------------------------------------------------ //
//  Shared config struct (must match the C# side exactly)             //
// ------------------------------------------------------------------ //
#pragma pack(push, 1)
struct EAMProxyConfig
{
    char  proxyHost[256];
    int   proxyPort;
    char  username[128];
    char  password[128];
    int   hasAuth;        // 1 if username/password are set
};
#pragma pack(pop)

// ------------------------------------------------------------------ //
//  Globals                                                            //
// ------------------------------------------------------------------ //
static EAMProxyConfig  g_config    = {};
static bool            g_hooked    = false;

// Typedefs for the original functions
typedef int (WSAAPI *connect_fn)   (SOCKET, const sockaddr*, int);
typedef int (WSAAPI *WSAConnect_fn)(SOCKET, const sockaddr*, int, LPWSABUF, LPWSABUF, LPQOS, LPQOS);

static connect_fn    g_orig_connect    = nullptr;
static WSAConnect_fn g_orig_WSAConnect = nullptr;

// ------------------------------------------------------------------ //
//  recv_exact: reads exactly `len` bytes, retrying on short reads    //
// ------------------------------------------------------------------ //
static bool recv_exact(SOCKET s, void* buf, int len)
{
    int total = 0;
    char* p = (char*)buf;
    while (total < len)
    {
        int r = recv(s, p + total, len - total, 0);
        if (r <= 0) return false;
        total += r;
    }
    return true;
}

// ------------------------------------------------------------------ //
//  SOCKS5 handshake helper                                            //
// ------------------------------------------------------------------ //
static bool socks5_connect(SOCKET s, const sockaddr* destAddr, int destAddrLen)
{
    // --- Greeting ---
    unsigned char greeting[4];
    int glen = 0;
    greeting[glen++] = 0x05;
    if (g_config.hasAuth)
    {
        greeting[glen++] = 0x02;
        greeting[glen++] = 0x00;
        greeting[glen++] = 0x02;
    }
    else
    {
        greeting[glen++] = 0x01;
        greeting[glen++] = 0x00;
    }

    if (send(s, (char*)greeting, glen, 0) != glen) return false;

    unsigned char choice[2] = {};
    if (!recv_exact(s, choice, 2)) return false;
    if (choice[0] != 0x05) return false;

    if (choice[1] == 0x02)
    {
        if (!g_config.hasAuth) return false;

        int ulen = (int)strlen(g_config.username);
        int plen = (int)strlen(g_config.password);

        std::string authMsg;
        authMsg.push_back(0x01);
        authMsg.push_back((char)ulen);
        authMsg.append(g_config.username, ulen);
        authMsg.push_back((char)plen);
        authMsg.append(g_config.password, plen);

        if (send(s, authMsg.data(), (int)authMsg.size(), 0) != (int)authMsg.size()) return false;

        unsigned char authReply[2] = {};
        if (!recv_exact(s, authReply, 2)) return false;
        if (authReply[1] != 0x00) return false;
    }
    else if (choice[1] != 0x00)
    {
        return false;
    }

    // --- CONNECT request ---
    std::string req;
    req.push_back(0x05);
    req.push_back(0x01); // CONNECT
    req.push_back(0x00);

    if (destAddr->sa_family == AF_INET)
    {
        const sockaddr_in* sin = (const sockaddr_in*)destAddr;
        req.push_back(0x01);
        req.append((char*)&sin->sin_addr.s_addr, 4);
        req.append((char*)&sin->sin_port, 2);
    }
    else if (destAddr->sa_family == AF_INET6)
    {
        const sockaddr_in6* sin6 = (const sockaddr_in6*)destAddr;
        req.push_back(0x04);
        req.append((char*)&sin6->sin6_addr, 16);
        req.append((char*)&sin6->sin6_port, 2);
    }
    else
    {
        return false;
    }

    if (send(s, req.data(), (int)req.size(), 0) != (int)req.size()) return false;

    // --- Read reply header ---
    unsigned char repHdr[4] = {};
    if (!recv_exact(s, repHdr, 4)) return false;
    if (repHdr[1] != 0x00) return false;

    // Consume bound address
    int skipBytes = 0;
    switch (repHdr[3])
    {
        case 0x01: skipBytes = 4;  break;
        case 0x03: {
            unsigned char dlen = 0;
            if (!recv_exact(s, &dlen, 1)) return false;
            skipBytes = dlen;
            break;
        }
        case 0x04: skipBytes = 16; break;
        default:   return false;
    }

    char tmp[256] = {};
    while (skipBytes > 0)
    {
        int chunk = std::min(skipBytes, (int)sizeof(tmp));
        if (!recv_exact(s, tmp, chunk)) return false;
        skipBytes -= chunk;
    }
    char portBuf[2] = {};
    if (!recv_exact(s, portBuf, 2)) return false;

    return true;
}

// ------------------------------------------------------------------ //
//  Hooked connect()                                                   //
// ------------------------------------------------------------------ //
static int WSAAPI hooked_connect(SOCKET s, const sockaddr* name, int namelen)
{
    int sockType = 0;
    int optLen   = sizeof(sockType);
    getsockopt(s, SOL_SOCKET, SO_TYPE, (char*)&sockType, &optLen);
    if (sockType != SOCK_STREAM)
        return g_orig_connect(s, name, namelen);

    // Skip IPv6 - proxy is IPv4 only; IPv6 connections are telemetry, not game servers
    if (name->sa_family == AF_INET6)
        return g_orig_connect(s, name, namelen);

    // Only intercept ROTMG ports: 443 (auth server) and 2050 (game server).
    // Everything else passes through directly.
    if (name->sa_family == AF_INET)
    {
        const sockaddr_in* sin4 = (const sockaddr_in*)name;
        u_short destPort = ntohs(sin4->sin_port);
        if (destPort != 2050 && destPort != 443)
            return g_orig_connect(s, name, namelen);
    }

    // Resolve proxy
    sockaddr_in proxyAddr = {};
    proxyAddr.sin_family = AF_INET;
    proxyAddr.sin_port   = htons((u_short)g_config.proxyPort);

    struct addrinfo hints = {}, *res = nullptr;
    hints.ai_family   = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    if (getaddrinfo(g_config.proxyHost, nullptr, &hints, &res) != 0)
        return g_orig_connect(s, name, namelen);

    proxyAddr.sin_addr = ((sockaddr_in*)res->ai_addr)->sin_addr;
    freeaddrinfo(res);

    // Force blocking for synchronous SOCKS5 handshake
    u_long nonBlocking = 0;
    ioctlsocket(s, FIONBIO, &nonBlocking);

    // Connect to proxy - always IPv4, always sizeof(sockaddr_in)
    int ret = g_orig_connect(s, (sockaddr*)&proxyAddr, sizeof(sockaddr_in));
    int wsaErr = WSAGetLastError();

    if (ret != 0 && wsaErr != WSAEISCONN)
    {
        nonBlocking = 1;
        ioctlsocket(s, FIONBIO, &nonBlocking);
        WSASetLastError(wsaErr);
        return ret;
    }

    // SOCKS5 handshake
    if (!socks5_connect(s, name, namelen))
    {
        nonBlocking = 1;
        ioctlsocket(s, FIONBIO, &nonBlocking);
        closesocket(s);
        WSASetLastError(WSAECONNREFUSED);
        return SOCKET_ERROR;
    }

    // Restore non-blocking
    nonBlocking = 1;
    ioctlsocket(s, FIONBIO, &nonBlocking);
    return 0;
}

// ------------------------------------------------------------------ //
//  Hooked WSAConnect()                                                //
// ------------------------------------------------------------------ //
static int WSAAPI hooked_WSAConnect(SOCKET s, const sockaddr* name, int namelen,
                                     LPWSABUF lpCallerData, LPWSABUF lpCalleeData,
                                     LPQOS lpSQOS, LPQOS lpGQOS)
{
    // Delegate to our hooked connect - WSAConnect with no data is identical
    if (lpCallerData == nullptr && lpCalleeData == nullptr)
        return hooked_connect(s, name, namelen);

    // If caller data is present, fall through to original (very rare in games)
    return g_orig_WSAConnect(s, name, namelen, lpCallerData, lpCalleeData, lpSQOS, lpGQOS);
}

// ------------------------------------------------------------------ //
//  IAT patcher                                                        //
// ------------------------------------------------------------------ //

/*
 * Patches IAT entries by comparing the live pointer value in each module's IAT
 * against the real exported address of funcName from targetDll.
 * This correctly handles ordinal imports (which Unity/IL2CPP uses for ws2_32)
 * where the OriginalFirstThunk name lookup would return nothing.
 */
static void* PatchIAT(const char* targetDll, const char* funcName, void* newFunc)
{
    // Resolve the real function address - this is what the IAT currently points at
    HMODULE hTargetDll = GetModuleHandleA(targetDll);
    if (!hTargetDll) return nullptr;

    void* realFunc = (void*)GetProcAddress(hTargetDll, funcName);
    if (!realFunc) return nullptr;

    HMODULE hMods[1024] = {};
    DWORD needed = 0;
    HANDLE hProc = GetCurrentProcess();

    typedef BOOL(WINAPI* EnumProcessModulesFn)(HANDLE, HMODULE*, DWORD, LPDWORD);
    HMODULE hPsapi = GetModuleHandleW(L"psapi.dll");
    if (!hPsapi) hPsapi = LoadLibraryW(L"psapi.dll");
    if (!hPsapi) return nullptr;

    auto enumMods = (EnumProcessModulesFn)GetProcAddress(hPsapi, "EnumProcessModules");
    if (!enumMods || !enumMods(hProc, hMods, sizeof(hMods), &needed)) return nullptr;

    DWORD modCount = needed / sizeof(HMODULE);

    for (DWORD i = 0; i < modCount; ++i)
    {
        BYTE* base = (BYTE*)hMods[i];

        IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) continue;

        IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) continue;

        IMAGE_DATA_DIRECTORY* importDir =
            &nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (!importDir->VirtualAddress) continue;

        IMAGE_IMPORT_DESCRIPTOR* desc =
            (IMAGE_IMPORT_DESCRIPTOR*)(base + importDir->VirtualAddress);

        for (; desc->Name; ++desc)
        {
            const char* dllName = (const char*)(base + desc->Name);
            if (_stricmp(dllName, targetDll) != 0) continue;

            IMAGE_THUNK_DATA* iatThunk =
                (IMAGE_THUNK_DATA*)(base + desc->FirstThunk);

            for (int j = 0; iatThunk[j].u1.Function; ++j)
            {
                // Match by current pointer value - works for both named and ordinal imports
                if ((void*)iatThunk[j].u1.Function != realFunc) continue;

                DWORD oldProt = 0;
                void** entry  = (void**)&iatThunk[j].u1.Function;
                VirtualProtect(entry, sizeof(void*), PAGE_READWRITE, &oldProt);
                *entry = newFunc;
                VirtualProtect(entry, sizeof(void*), oldProt, &oldProt);
            }
        }
    }
    return realFunc;
}

// ------------------------------------------------------------------ //
//  DLL entry point                                                    //
// ------------------------------------------------------------------ //
BOOL WINAPI DllMain(HINSTANCE hInst, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;

    DisableThreadLibraryCalls(hInst);

    // --- Read proxy config from shared memory ---
    char shmName[64] = {};
    DWORD pid = GetCurrentProcessId();
    sprintf_s(shmName, "EAMProxyConfig_%lu", pid);

    HANDLE hMap = OpenFileMappingA(FILE_MAP_READ, FALSE, shmName);
    if (!hMap) return TRUE; // no config = no proxy, exit cleanly

    EAMProxyConfig* cfg = (EAMProxyConfig*)MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, sizeof(EAMProxyConfig));
    if (cfg)
    {
        memcpy(&g_config, cfg, sizeof(EAMProxyConfig));
        UnmapViewOfFile(cfg);
    }
    CloseHandle(hMap);

    if (g_config.proxyHost[0] == '\0' || g_config.proxyPort == 0)
        return TRUE; // empty config, nothing to do

    // --- Install IAT hooks ---
    g_orig_connect    = (connect_fn)   PatchIAT("ws2_32.dll", "connect",    (void*)hooked_connect);
    g_orig_WSAConnect = (WSAConnect_fn)PatchIAT("ws2_32.dll", "WSAConnect", (void*)hooked_WSAConnect);

    // Fallback: get originals directly from ws2_32 if IAT patching didn't find them
    if (!g_orig_connect)
        g_orig_connect = (connect_fn)GetProcAddress(GetModuleHandleW(L"ws2_32.dll"), "connect");
    if (!g_orig_WSAConnect)
        g_orig_WSAConnect = (WSAConnect_fn)GetProcAddress(GetModuleHandleW(L"ws2_32.dll"), "WSAConnect");

    g_hooked = (g_orig_connect != nullptr);
    return TRUE;
}
