using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Realmbox.Core.AccessToken;
using Realmbox.Core.Exceptions;
using Realmbox.Core.Settings;

namespace Realmbox.Core.Util
{
    /// <summary>
    /// Provides helper methods for encoding, decoding, and launching the Exalt client.
    ///
    /// Proxy strategy:
    ///   - Accounts WITHOUT a proxy: launched normally, no interception.
    ///   - Accounts WITH a proxy:
    ///       1. A local Socks5Tunnel is started (loopback → remote proxy) for the
    ///          access token HTTP request only.
    ///       2. The game process is started normally, then after a short settle delay
    ///          ProxyHook.dll is injected via CreateRemoteThread(LoadLibraryW).
    ///       3. The DLL reads proxy config from named shared memory and patches the
    ///          IAT of ws2_32!connect + ws2_32!WSAConnect - every connection the game
    ///          makes for its entire lifetime (including server switches) goes through
    ///          the SOCKS5 proxy.
    /// </summary>
    public class Helper
    {
        // Keep tunnels alive for the token request (short-lived)
        private static readonly Dictionary<string, Socks5Tunnel> _activeTunnels = new();
        private static readonly object _tunnelLock = new();

        public static string Base64Encode(string plainText)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }

        public static string Base64Decode(string encodedText)
        {
            byte[] data = Convert.FromBase64String(encodedText);
            return Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Returns the directory that contains this assembly's DLL,
        /// which is where ProxyHook.dll will be placed at publish time.
        /// </summary>
        private static string GetAssemblyDirectory()
        {
            string? loc = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return loc ?? AppContext.BaseDirectory;
        }

        public static async Task LaunchExaltClient(
            string exaltPath,
            string email,
            string password,
            string manualDeviceToken,
            Account? account = null)
        {
            string deviceToken = string.IsNullOrEmpty(manualDeviceToken)
                ? GetDeviceToken()
                : manualDeviceToken;

            bool hasProxy = account?.HasProxy == true;

            // ----------------------------------------------------------------
            // 1. Fetch access token
            //    Route through SOCKS5 tunnel if proxy is configured so the
            //    login request also comes from the proxy IP.
            // ----------------------------------------------------------------
            Socks5Tunnel? tokenTunnel = null;
            if (hasProxy)
            {
                StopTunnelForAccount(account!.Name);
                tokenTunnel = new Socks5Tunnel(
                    account.ProxyHost!,
                    account.ProxyPort!.Value,
                    account.ProxyUsername,
                    account.ProxyPassword);
                await tokenTunnel.StartAsync().ConfigureAwait(false);
                lock (_tunnelLock) _activeTunnels[account.Name] = tokenTunnel;
            }

            AccessTokenRequest req = new(email, password, deviceToken);
            AccessTokenResponse resp = await RequestHelper.RequestAccessToken(
                req,
                hasProxy ? account!.ProxyHost    : null,
                hasProxy ? account!.ProxyPort     : null,
                hasProxy ? account!.ProxyUsername : null,
                hasProxy ? account!.ProxyPassword : null
            ).ConfigureAwait(false);

            // Token acquired - stop the short-lived tunnel
            if (tokenTunnel != null)
            {
                tokenTunnel.Stop();
                lock (_tunnelLock) _activeTunnels.Remove(account!.Name);
            }

            // ----------------------------------------------------------------
            // 2. Launch game
            // ----------------------------------------------------------------
            if (string.IsNullOrEmpty(exaltPath))
                throw new ExaltPathNotFoundException();

            string exaltExe = exaltPath + @"\RotMG Exalt.exe";
            string gameArgs =
                $"data:{{platform:Deca," +
                $"guid:{Base64Encode(email)}," +
                $"token:{Base64Encode(resp.AccessToken)}," +
                $"tokenTimestamp:{Base64Encode(resp.AccessTokenTimestamp)}," +
                $"tokenExpiration:{Base64Encode(resp.AccessTokenExpiration.ToString())}," +
                $"env:4}}";

            Process? gameProcess;
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName       = exaltExe,
                    Arguments      = gameArgs,
                    UseShellExecute = true,
                };
                gameProcess = Process.Start(psi);
            }
            catch
            {
                throw new ExaltExeNotFoundException();
            }

            // ----------------------------------------------------------------
            // 3. Inject ProxyHook.dll (proxy accounts only)
            // ----------------------------------------------------------------
            if (hasProxy && gameProcess != null)
            {
                string dllPath = Path.Combine(GetAssemblyDirectory(), "ProxyHook.dll");

                // Wait for the process to finish initialising its modules.
                // Unity IL2CPP games take a moment before ws2_32 is loaded.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Give the game time to load all its DLLs and reach
                        // its first network connection attempt.
                        // Unity IL2CPP games typically take 5-10s before ws2_32 is used.
                        await Task.Delay(8000).ConfigureAwait(false);

                        if (gameProcess.HasExited) return;

                        DllInjector.Inject(
                            gameProcess,
                            dllPath,
                            account!.ProxyHost!,
                            account.ProxyPort!.Value,
                            account.ProxyUsername,
                            account.ProxyPassword);
                    }
                    catch (Exception ex)
                    {
                        // Log to debug output - don't crash the UI thread
                        System.Diagnostics.Debug.WriteLine(
                            $"[EAM] ProxyHook injection failed for {account?.Name}: {ex.Message}");
                    }
                });
            }
        }

        public static void StopTunnelForAccount(string accountName)
        {
            lock (_tunnelLock)
            {
                if (_activeTunnels.TryGetValue(accountName, out Socks5Tunnel? t))
                {
                    t.Stop();
                    _activeTunnels.Remove(accountName);
                }
            }
        }

        public static void StopAllTunnels()
        {
            lock (_tunnelLock)
            {
                foreach (var t in _activeTunnels.Values) t.Stop();
                _activeTunnels.Clear();
            }
        }

        private static string GetDeviceToken()
        {
            string hw = "";
            foreach (string item in new[] { "Win32_BaseBoard", "Win32_BIOS", "Win32_OperatingSystem" })
                foreach (ManagementBaseObject? o in new ManagementObjectSearcher($"SELECT * FROM {item}").Get())
                    hw += o.GetPropertyValue("SerialNumber").ToString() ?? "";
            return Hash(hw);
        }

        private static string Hash(string input)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        /// <summary>Kills every running RotMG Exalt process.</summary>
        public static int CloseAllClients()
        {
            Process[] procs = Process.GetProcessesByName("RotMG Exalt");
            foreach (Process p in procs)
            {
                try { p.Kill(); } catch { /* already gone */ }
            }
            return procs.Length;
        }
    }
}
