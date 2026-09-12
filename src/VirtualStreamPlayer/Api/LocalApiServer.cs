using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VirtualStreamPlayer.Ipc;
using VirtualStreamPlayer.Logging;
using VirtualStreamPlayer.Streaming;

namespace VirtualStreamPlayer.Api
{
    /// <summary>
    /// Local-only HTTP API (127.0.0.1) so a consumer program (or a quick curl/
    /// browser check) can discover the pipe name/format and query status without
    /// needing to parse the named pipe protocol just to know if it's connected.
    /// Never bound to 0.0.0.0 - not reachable from the network.
    /// </summary>
    public class LocalApiServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly PlaybackEngine _engine;
        private readonly NamedPipeAudioServer _pipeServer;
        private CancellationTokenSource? _cts;

        public LocalApiServer(int port, PlaybackEngine engine, NamedPipeAudioServer pipeServer)
        {
            _engine = engine;
            _pipeServer = pipeServer;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public void Start()
        {
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_cts.Token));
            Logger.Info($"API local escuchando en {string.Join(", ", _listener.Prefixes)}");
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    if (ct.IsCancellationRequested) break;
                    continue;
                }
                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                object? result = path switch
                {
                    "/api/status" => new
                    {
                        state = _engine.State.ToString(),
                        url = _engine.CurrentUrl,
                        station = _engine.StationName,
                        connectedSince = _engine.ConnectedSince,
                        bytesDecoded = _engine.BytesDecoded,
                        reconnectAttempts = _engine.ReconnectAttempts,
                        pipeClients = _pipeServer.ConnectedClientCount
                    },
                    "/api/metadata" => new { title = _engine.CurrentTitle },
                    "/api/format" => _engine.CurrentFormat == null
                        ? new { ready = false }
                        : new
                        {
                            ready = true,
                            sampleRate = _engine.CurrentFormat.SampleRate,
                            channels = _engine.CurrentFormat.Channels,
                            bitsPerSample = _engine.CurrentFormat.BitsPerSample
                        },
                    "/api/pipe" => new
                    {
                        pipeName = _pipeServer.PipeName,
                        fullPath = $@"\\.\pipe\{_pipeServer.PipeName}",
                        connectedClients = _pipeServer.ConnectedClientCount
                    },
                    _ when path == "/api/connect" && ctx.Request.HttpMethod == "POST" =>
                        await HandleConnectAsync(ctx),
                    _ when path == "/api/disconnect" && ctx.Request.HttpMethod == "POST" =>
                        HandleDisconnect(),
                    _ => null
                };

                if (result == null)
                {
                    ctx.Response.StatusCode = 404;
                    await WriteJsonAsync(ctx, new { error = "not found" });
                }
                else
                {
                    await WriteJsonAsync(ctx, result);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error manejando request de API local", ex);
                try
                {
                    ctx.Response.StatusCode = 500;
                    await WriteJsonAsync(ctx, new { error = ex.Message });
                }
                catch { /* ignore */ }
            }
        }

        private async Task<object> HandleConnectAsync(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                return new { error = "falta 'url' en el body" };

            _engine.Start(url);
            return new { ok = true, url };
        }

        private object HandleDisconnect()
        {
            _engine.Stop();
            return new { ok = true };
        }

        private static async Task WriteJsonAsync(HttpListenerContext ctx, object payload)
        {
            ctx.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.OutputStream.Close();
        }

        public void Dispose() => Stop();
    }
}
