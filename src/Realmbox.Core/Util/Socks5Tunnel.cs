using System.Net;
using System.Net.Sockets;

namespace Realmbox.Core.Util
{
    /// <summary>
    /// Binds a local SOCKS5 listener on a free loopback port and transparently
    /// forwards every connection to a remote SOCKS5 proxy server.
    ///
    /// This lets multiple game instances each get a unique localhost port while
    /// each port tunnels to a different remote proxy - solving the problem of
    /// process-name-based proxy tools (e.g. Proxifier) that cannot differentiate
    /// between multiple instances of the same executable.
    ///
    /// Usage:
    ///   var tunnel = new Socks5Tunnel(remoteHost, remotePort, user, pass);
    ///   await tunnel.StartAsync();
    ///   int localPort = tunnel.LocalPort;   // set ALL_PROXY=socks5://127.0.0.1:localPort
    ///   // ... later ...
    ///   tunnel.Stop();
    /// </summary>
    public sealed class Socks5Tunnel : IDisposable
    {
        private readonly string _remoteHost;
        private readonly int _remotePort;
        private readonly string? _username;
        private readonly string? _password;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public int LocalPort { get; private set; }

        public Socks5Tunnel(string remoteHost, int remotePort, string? username = null, string? password = null)
        {
            _remoteHost = remoteHost;
            _remotePort = remotePort;
            _username = username;
            _password = password;
        }

        /// <summary>
        /// Binds to a free loopback port and begins accepting connections in the background.
        /// </summary>
        public Task StartAsync()
        {
            _cts = new CancellationTokenSource();

            // Port 0 → OS assigns a free port
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // Run accept loop on a background thread - fire and forget
            _ = AcceptLoopAsync(_cts.Token);

            return Task.CompletedTask;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        // ------------------------------------------------------------------ //
        //  Accept loop                                                         //
        // ------------------------------------------------------------------ //

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    // Handle each connection concurrently
                    _ = HandleClientAsync(client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch { /* listener stopped */ break; }
            }
        }

        // ------------------------------------------------------------------ //
        //  Per-connection handler                                              //
        // ------------------------------------------------------------------ //

        private async Task HandleClientAsync(TcpClient localClient, CancellationToken ct)
        {
            using (localClient)
            using (TcpClient remoteClient = new())
            {
                try
                {
                    localClient.NoDelay = true;
                    remoteClient.NoDelay = true;

                    // Connect to the remote SOCKS5 proxy
                    await remoteClient.ConnectAsync(_remoteHost, _remotePort, ct).ConfigureAwait(false);

                    NetworkStream localStream  = localClient.GetStream();
                    NetworkStream remoteStream = remoteClient.GetStream();

                    // Perform SOCKS5 handshake with the remote proxy on behalf of
                    // the game client. The game sends a standard SOCKS5 greeting;
                    // we relay it after (optionally) authenticating with the proxy.
                    await Socks5HandshakeWithRemoteAsync(localStream, remoteStream, ct).ConfigureAwait(false);

                    // After handshake, pipe data in both directions until one side closes.
                    await Task.WhenAny(
                        PipeAsync(localStream,  remoteStream, ct),
                        PipeAsync(remoteStream, localStream,  ct)
                    ).ConfigureAwait(false);
                }
                catch { /* connection closed or refused - silently drop */ }
            }
        }

        // ------------------------------------------------------------------ //
        //  SOCKS5 handshake relay                                             //
        //                                                                     //
        //  The local game client believes it is talking to a plain SOCKS5    //
        //  proxy at 127.0.0.1:LocalPort.  We sit in the middle:              //
        //                                                                     //
        //    game  <--SOCKS5-->  [tunnel]  <--SOCKS5+auth-->  remote proxy   //
        //                                                                     //
        //  Steps:                                                             //
        //    1. Read the client's greeting (method list)                      //
        //    2. Authenticate with the remote proxy (if credentials set)       //
        //    3. Tell the client "no auth required" so it sends the request    //
        //    4. Read the client's CONNECT request                             //
        //    5. Forward the CONNECT request to the remote proxy               //
        //    6. Relay the remote proxy's reply back to the client             //
        // ------------------------------------------------------------------ //

        private async Task Socks5HandshakeWithRemoteAsync(
            NetworkStream localStream,
            NetworkStream remoteStream,
            CancellationToken ct)
        {
            // --- Step 1: Read client greeting ---
            // +----+----------+----------+
            // |VER | NMETHODS | METHODS  |
            // +----+----------+----------+
            // | 1  |    1     | 1 to 255 |
            byte[] hdrBuf = new byte[2];
            await ReadExactAsync(localStream, hdrBuf, ct).ConfigureAwait(false);

            if (hdrBuf[0] != 0x05)
                throw new InvalidDataException("Not a SOCKS5 client.");

            int nMethods = hdrBuf[1];
            byte[] methods = new byte[nMethods];
            await ReadExactAsync(localStream, methods, ct).ConfigureAwait(false);
            // We don't need to inspect methods - we'll tell client "no auth".

            // --- Step 2: Authenticate with remote proxy (if needed) ---
            bool needsAuth = !string.IsNullOrEmpty(_username);

            if (needsAuth)
            {
                // Offer username/password auth (0x02) to remote
                byte[] remoteGreeting = [0x05, 0x01, 0x02];
                await remoteStream.WriteAsync(remoteGreeting, ct).ConfigureAwait(false);

                byte[] remoteChoice = new byte[2];
                await ReadExactAsync(remoteStream, remoteChoice, ct).ConfigureAwait(false);

                if (remoteChoice[1] == 0x02)
                {
                    // Send username/password sub-negotiation
                    byte[] user = System.Text.Encoding.UTF8.GetBytes(_username!);
                    byte[] pass = System.Text.Encoding.UTF8.GetBytes(_password ?? "");

                    byte[] authMsg = new byte[3 + user.Length + pass.Length];
                    authMsg[0] = 0x01;          // auth sub-negotiation version
                    authMsg[1] = (byte)user.Length;
                    Buffer.BlockCopy(user, 0, authMsg, 2, user.Length);
                    authMsg[2 + user.Length] = (byte)pass.Length;
                    Buffer.BlockCopy(pass, 0, authMsg, 3 + user.Length, pass.Length);

                    await remoteStream.WriteAsync(authMsg, ct).ConfigureAwait(false);

                    byte[] authReply = new byte[2];
                    await ReadExactAsync(remoteStream, authReply, ct).ConfigureAwait(false);

                    if (authReply[1] != 0x00)
                        throw new Exception("SOCKS5 proxy authentication failed.");
                }
                else if (remoteChoice[1] == 0xFF)
                {
                    throw new Exception("Remote SOCKS5 proxy rejected all auth methods.");
                }
            }
            else
            {
                // Offer no-auth (0x00) to remote
                byte[] remoteGreeting = [0x05, 0x01, 0x00];
                await remoteStream.WriteAsync(remoteGreeting, ct).ConfigureAwait(false);

                byte[] remoteChoice = new byte[2];
                await ReadExactAsync(remoteStream, remoteChoice, ct).ConfigureAwait(false);

                if (remoteChoice[1] == 0xFF)
                    throw new Exception("Remote SOCKS5 proxy requires authentication but none was configured.");
            }

            // --- Step 3: Tell local client "no auth required" ---
            await localStream.WriteAsync(new byte[] { 0x05, 0x00 }, ct).ConfigureAwait(false);

            // --- Step 4: Read the client's CONNECT request ---
            // +----+-----+-------+------+----------+----------+
            // |VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
            // +----+-----+-------+------+----------+----------+
            byte[] reqHdr = new byte[4];
            await ReadExactAsync(localStream, reqHdr, ct).ConfigureAwait(false);

            // Read destination address
            byte atyp = reqHdr[3];
            byte[] addrBytes;
            switch (atyp)
            {
                case 0x01: // IPv4
                    addrBytes = new byte[4];
                    await ReadExactAsync(localStream, addrBytes, ct).ConfigureAwait(false);
                    break;
                case 0x03: // Domain name
                    byte[] lenBuf = new byte[1];
                    await ReadExactAsync(localStream, lenBuf, ct).ConfigureAwait(false);
                    addrBytes = new byte[1 + lenBuf[0]];
                    addrBytes[0] = lenBuf[0];
                    await ReadExactAsync(localStream, addrBytes, 1, lenBuf[0], ct).ConfigureAwait(false);
                    break;
                case 0x04: // IPv6
                    addrBytes = new byte[16];
                    await ReadExactAsync(localStream, addrBytes, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported SOCKS5 address type: {atyp}");
            }

            byte[] portBytes = new byte[2];
            await ReadExactAsync(localStream, portBytes, ct).ConfigureAwait(false);

            // --- Step 5: Forward the full CONNECT request to remote proxy ---
            // Reassemble the request
            int totalLen = 4 + addrBytes.Length + 2;
            byte[] fullReq = new byte[totalLen];
            fullReq[0] = 0x05;
            fullReq[1] = reqHdr[1]; // CMD (usually 0x01 = CONNECT)
            fullReq[2] = 0x00;      // RSV
            fullReq[3] = atyp;
            Buffer.BlockCopy(addrBytes, 0, fullReq, 4, addrBytes.Length);
            Buffer.BlockCopy(portBytes, 0, fullReq, 4 + addrBytes.Length, 2);

            await remoteStream.WriteAsync(fullReq, ct).ConfigureAwait(false);

            // --- Step 6: Read remote proxy reply and forward to client ---
            // +----+-----+-------+------+----------+----------+
            // |VER | REP |  RSV  | ATYP | BND.ADDR | BND.PORT |
            byte[] repHdr = new byte[4];
            await ReadExactAsync(remoteStream, repHdr, ct).ConfigureAwait(false);

            // Read bound address from reply
            byte repAtyp = repHdr[3];
            byte[] repAddr;
            switch (repAtyp)
            {
                case 0x01: repAddr = new byte[4];  await ReadExactAsync(remoteStream, repAddr, ct).ConfigureAwait(false); break;
                case 0x03:
                    byte[] rl = new byte[1];
                    await ReadExactAsync(remoteStream, rl, ct).ConfigureAwait(false);
                    repAddr = new byte[1 + rl[0]];
                    repAddr[0] = rl[0];
                    await ReadExactAsync(remoteStream, repAddr, 1, rl[0], ct).ConfigureAwait(false);
                    break;
                case 0x04: repAddr = new byte[16]; await ReadExactAsync(remoteStream, repAddr, ct).ConfigureAwait(false); break;
                default:   repAddr = new byte[4];  await ReadExactAsync(remoteStream, repAddr, ct).ConfigureAwait(false); break;
            }

            byte[] repPort = new byte[2];
            await ReadExactAsync(remoteStream, repPort, ct).ConfigureAwait(false);

            // Relay reply to client
            int repTotalLen = 4 + repAddr.Length + 2;
            byte[] fullRep = new byte[repTotalLen];
            fullRep[0] = 0x05;
            fullRep[1] = repHdr[1]; // REP code
            fullRep[2] = 0x00;
            fullRep[3] = repAtyp;
            Buffer.BlockCopy(repAddr, 0, fullRep, 4, repAddr.Length);
            Buffer.BlockCopy(repPort, 0, fullRep, 4 + repAddr.Length, 2);

            await localStream.WriteAsync(fullRep, ct).ConfigureAwait(false);

            if (repHdr[1] != 0x00)
                throw new Exception($"Remote SOCKS5 proxy refused connection (REP=0x{repHdr[1]:X2}).");

            // Handshake complete - PipeAsync takes over for raw data relay.
        }

        // ------------------------------------------------------------------ //
        //  Raw bidirectional pipe                                             //
        // ------------------------------------------------------------------ //

        private static async Task PipeAsync(Stream source, Stream destination, CancellationToken ct)
        {
            byte[] buffer = new byte[81920];
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
            catch { /* either side closed */ }
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
            => await ReadExactAsync(stream, buffer, 0, buffer.Length, ct).ConfigureAwait(false);

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Connection closed during SOCKS5 handshake.");
                totalRead += read;
            }
        }
    }
}
