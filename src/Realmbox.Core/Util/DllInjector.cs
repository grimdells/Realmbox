using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Realmbox.Core.Util
{
    /// <summary>
    /// Injects ProxyHook.dll into a running process using the classic
    /// VirtualAllocEx + WriteProcessMemory + CreateRemoteThread(LoadLibraryW) technique.
    ///
    /// Before injecting, the proxy configuration is written to a named shared memory
    /// block ("EAMProxyConfig_&lt;PID&gt;") which the DLL reads on DllMain attach.
    /// </summary>
    public static class DllInjector
    {
        // ------------------------------------------------------------------ //
        //  Shared config layout - must match EAMProxyConfig in proxyhook.cpp  //
        // ------------------------------------------------------------------ //

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        private struct EAMProxyConfig
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProxyHost;

            public int ProxyPort;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Username;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Password;

            public int HasAuth;  // 1 if credentials present
        }

        // ------------------------------------------------------------------ //
        //  Win32 P/Invokes                                                    //
        // ------------------------------------------------------------------ //

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddr,
            uint dwSize, uint allocType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBase,
            byte[] buffer, uint size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr attr,
            uint stackSize, IntPtr startAddr, IntPtr param, uint flags, out uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr CreateFileMappingA(IntPtr hFile, IntPtr attr,
            uint protect, uint maxHigh, uint maxLow, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr hMap, uint access,
            uint offsetHigh, uint offsetLow, uint bytes);

        [DllImport("kernel32.dll")]
        private static extern bool UnmapViewOfFile(IntPtr addr);

        private const uint PROCESS_ALL_ACCESS    = 0x1F0FFF;
        private const uint MEM_COMMIT_RESERVE    = 0x3000;
        private const uint PAGE_READWRITE        = 0x04;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint FILE_MAP_ALL_ACCESS   = 0xF001F;
        private const uint PAGE_READWRITE_SHM    = 0x04;
        private const uint WAIT_TIMEOUT          = 0x00000102;

        // ------------------------------------------------------------------ //
        //  Public API                                                         //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Writes proxy config to shared memory then injects ProxyHook.dll into
        /// the given process. Call this after the game process has started and
        /// its main module is loaded (wait ~1-2 seconds after Process.Start).
        /// </summary>
        /// <param name="process">The game process to inject into.</param>
        /// <param name="dllPath">Full path to ProxyHook.dll on disk.</param>
        /// <param name="proxyHost">Remote SOCKS5 proxy hostname/IP.</param>
        /// <param name="proxyPort">Remote SOCKS5 proxy port.</param>
        /// <param name="username">Optional username (null/empty = no auth).</param>
        /// <param name="password">Optional password.</param>
        public static void Inject(
            Process process,
            string dllPath,
            string proxyHost,
            int proxyPort,
            string? username = null,
            string? password = null)
        {
            uint pid = (uint)process.Id;

            // 1. Write config to named shared memory
            WriteSharedMemoryConfig(pid, proxyHost, proxyPort, username, password);

            // 2. Inject the DLL
            InjectDll(pid, dllPath);
        }

        // ------------------------------------------------------------------ //
        //  Private helpers                                                    //
        // ------------------------------------------------------------------ //

        private static void WriteSharedMemoryConfig(
            uint pid, string proxyHost, int proxyPort,
            string? username, string? password)
        {
            string shmName = $"EAMProxyConfig_{pid}";

            EAMProxyConfig cfg = new()
            {
                ProxyHost = proxyHost,
                ProxyPort = proxyPort,
                Username  = username  ?? "",
                Password  = password  ?? "",
                HasAuth   = string.IsNullOrEmpty(username) ? 0 : 1,
            };

            int structSize = Marshal.SizeOf<EAMProxyConfig>();

            IntPtr hMap = CreateFileMappingA(
                new IntPtr(-1),   // INVALID_HANDLE_VALUE → backed by pagefile
                IntPtr.Zero,
                PAGE_READWRITE_SHM,
                0,
                (uint)structSize,
                shmName);

            if (hMap == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateFileMapping failed: {Marshal.GetLastWin32Error()}");

            IntPtr view = MapViewOfFile(hMap, FILE_MAP_ALL_ACCESS, 0, 0, (uint)structSize);
            if (view == IntPtr.Zero)
            {
                CloseHandle(hMap);
                throw new InvalidOperationException(
                    $"MapViewOfFile failed: {Marshal.GetLastWin32Error()}");
            }

            // Marshal struct into the shared memory view
            Marshal.StructureToPtr(cfg, view, false);
            UnmapViewOfFile(view);

            // Keep hMap open - the DLL will open it by name and read it.
            // We leak the handle intentionally; it will be released when EAM exits.
            // (Alternatively store and close after a delay - kept simple here.)
        }

        private static void InjectDll(uint pid, string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException($"ProxyHook.dll not found at: {dllPath}");

            // Need a full absolute path
            dllPath = Path.GetFullPath(dllPath);

            IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
            if (hProcess == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"OpenProcess failed (PID {pid}): {Marshal.GetLastWin32Error()}. " +
                    "Try running EAM as Administrator.");

            try
            {
                // Encode path as UTF-16LE (LoadLibraryW)
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");

                // Allocate memory in remote process for the DLL path string
                IntPtr remoteStr = VirtualAllocEx(
                    hProcess, IntPtr.Zero,
                    (uint)pathBytes.Length,
                    MEM_COMMIT_RESERVE, PAGE_READWRITE);

                if (remoteStr == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"VirtualAllocEx failed: {Marshal.GetLastWin32Error()}");

                // Write path into remote process
                if (!WriteProcessMemory(hProcess, remoteStr, pathBytes,
                        (uint)pathBytes.Length, out _))
                    throw new InvalidOperationException(
                        $"WriteProcessMemory failed: {Marshal.GetLastWin32Error()}");

                // Get address of LoadLibraryW in kernel32
                // (same address in all 64-bit processes due to ASLR base sharing)
                IntPtr hKernel32    = GetModuleHandle("kernel32.dll");
                IntPtr loadLibraryW = GetProcAddress(hKernel32, "LoadLibraryW");

                if (loadLibraryW == IntPtr.Zero)
                    throw new InvalidOperationException("Could not find LoadLibraryW.");

                // Create remote thread that calls LoadLibraryW(dllPath)
                IntPtr hThread = CreateRemoteThread(
                    hProcess, IntPtr.Zero, 0,
                    loadLibraryW, remoteStr,
                    0, out _);

                if (hThread == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"CreateRemoteThread failed: {Marshal.GetLastWin32Error()}. " +
                        "Try running EAM as Administrator.");

                // Wait up to 10s for LoadLibrary to complete
                WaitForSingleObject(hThread, 10_000);
                CloseHandle(hThread);
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
    }
}
