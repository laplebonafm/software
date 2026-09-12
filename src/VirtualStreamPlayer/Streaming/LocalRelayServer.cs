using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VirtualStreamPlayer.Logging;

namespace VirtualStreamPlayer.Streaming
{
    /// <summary>
    /// Windows Media Foundation can open an HTTP URL directly and decode
    /// whatever codec is inside (MP3, AAC/AAC+, ...), but it does not know
    /// about the SHOUTcast/Icecast "icy-metaint" inline metadata trick, so it
    /// can't be pointed at the original station URL when that's in use - the
    /// embedded metadata blocks would be decoded as if they were audio and
    /// corrupt the sound every "metaint" bytes.
    ///
    /// This class re-serves an already-clean audio byte stream (metadata
    /// already stripped by <see cref="IcyMetadataStream"/>) as a plain local
    /// HTTP stream on 127.0.0.1, so MediaFoundationReader can open THAT
    /// instead and never has to deal with ICY at all. Only reachable on
    /// loopback - never exposed to the network.
    /// </summary>
    public class LocalRelayServer : IDisposable
    {
        private readonly Stream _source;
        private readonly string _contentType;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        public int Port { get; private set; }

        public LocalRelayServer(Stream source, string contentType)
        {
            _source = source;
            _contentType = string.IsNullOrWhiteSpace(contentType) ? "audio/mpeg" : contentType;
        }

        /// <summary>Starts the relay and returns the local URL to hand to MediaFoundationReader.</summary>
        public string Start()
        {
            Port = GetFreeLoopbackPort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ServeSingleConsumerAsync(_cts.Token));
            return $"http://127.0.0.1:{Port}/live";
        }

        private async Task ServeSingleConsumerAsync(CancellationToken ct)
        {
            try
            {
                // Only one consumer is ever expected (our own MediaFoundationReader),
                // so we just serve the first request that comes in.
                var ctx = await _listener!.GetContextAsync();
                ctx.Response.ContentType = _contentType;
                ctx.Response.SendChunked = true;
                ctx.Response.StatusCode = 200;

                var buffer = new byte[8192];
                while (!ct.IsCancellationRequested)
                {
                    int read = await _source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0) break;
                    await ctx.Response.OutputStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    await ctx.Response.OutputStream.FlushAsync(ct);
                }
            }
            catch (Exception)
            {
                // Origin dropped, MF consumer disconnected, or cancelled - all normal shutdown paths.
            }
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }
            try { _source.Dispose(); } catch { /* ignore */ }
        }
    }
}
