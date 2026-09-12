using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using VirtualStreamPlayer.Audio;
using VirtualStreamPlayer.Logging;

namespace VirtualStreamPlayer.Streaming
{
    public enum PlaybackState
    {
        Stopped,
        Connecting,
        Playing,
        Reconnecting
    }

    /// <summary>
    /// Owns the full pipeline: open the streaming URL, decode it to PCM, and
    /// keep it running forever with automatic reconnection on any failure
    /// (network drop, server hiccup, decode error). Does NOT touch any audio
    /// device - it only produces PCM bytes and format info for whoever is
    /// listening to <see cref="PcmChunkReady"/> (the named pipe server).
    ///
    /// Decoding is done through Windows Media Foundation
    /// (NAudio.Wave.MediaFoundationReader), which natively supports MP3,
    /// AAC/AAC+ and HLS (.m3u8) on Windows 8.1+ - so all three formats share
    /// the same code path:
    ///
    ///   - HLS (.m3u8): the playlist/segment fetching is entirely handled by
    ///     Media Foundation, so the original URL is handed to it directly.
    ///   - Direct MP3/AAC (Shoutcast/Icecast style): these almost always use
    ///     the "icy-metaint" inline metadata trick, which Media Foundation
    ///     does not understand. So the station is opened once here, its
    ///     inline metadata is stripped by <see cref="IcyMetadataStream"/>,
    ///     and the clean bytes are re-served locally by
    ///     <see cref="LocalRelayServer"/> - THAT loopback URL is what gets
    ///     handed to Media Foundation, whether the codec inside is MP3 or AAC.
    /// </summary>
    public class PlaybackEngine : IDisposable
    {
        private readonly StreamClient _streamClient = new();
        private CancellationTokenSource? _cts;
        private Task? _runTask;

        public string? CurrentUrl { get; private set; }
        public PlaybackState State { get; private set; } = PlaybackState.Stopped;
        public AudioFormatInfo? CurrentFormat { get; private set; }
        public string? CurrentTitle { get; private set; }
        public string? StationName { get; private set; }
        public long BytesDecoded { get; private set; }
        public int ReconnectAttempts { get; private set; }
        public DateTime? ConnectedSince { get; private set; }

        public int MaxReconnectDelaySeconds { get; set; } = 30;

        public event Action<PlaybackState>? StateChanged;
        public event Action<AudioFormatInfo>? FormatReady;
        public event Action<byte[]>? PcmChunkReady;
        public event Action<string>? TitleChanged;
        public event Action<string>? Log;

        public void Start(string url)
        {
            Stop();
            CurrentUrl = url;
            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunLoopAsync(url, _cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _runTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
            _cts = null;
            _runTask = null;
            CurrentFormat = null;
            SetState(PlaybackState.Stopped);
            ConnectedSince = null;
        }

        private async Task RunLoopAsync(string url, CancellationToken ct)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                SetState(attempt == 0 ? PlaybackState.Connecting : PlaybackState.Reconnecting);
                ReconnectAttempts = attempt;

                try
                {
                    await PlayOnceAsync(url, ct);
                    if (ct.IsCancellationRequested) break;
                    Log?.Invoke("El stream terminó de forma inesperada, reconectando...");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error("Error en la reproducción, se intentará reconectar", ex);
                    Log?.Invoke($"Error: {ex.Message} — reconectando...");
                }

                attempt++;
                ConnectedSince = null;
                CurrentFormat = null;
                int delaySeconds = Math.Min(MaxReconnectDelaySeconds, (int)Math.Pow(2, Math.Min(attempt, 6)));
                SetState(PlaybackState.Reconnecting);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PlayOnceAsync(string url, CancellationToken ct)
        {
            if (IsHlsPlaylist(url))
            {
                // HLS: Media Foundation fetches the playlist and every segment itself.
                Log?.Invoke("Detectado playlist HLS (.m3u8) — Media Foundation maneja los segmentos.");
                await PlayViaMediaFoundationAsync(url, ct);
                return;
            }

            var opened = await _streamClient.OpenAsync(url, ct);
            StationName = opened.StationName;
            opened.AudioStream.MetadataChanged += title =>
            {
                CurrentTitle = title;
                TitleChanged?.Invoke(title);
            };

            using var relay = new LocalRelayServer(opened.AudioStream, opened.ContentType ?? "audio/mpeg");
            var relayUrl = relay.Start();

            Log?.Invoke($"Conectado a {url}" + (StationName != null ? $" ({StationName})" : "") +
                        $" — tipo: {opened.ContentType ?? "desconocido"}");

            await PlayViaMediaFoundationAsync(relayUrl, ct);
        }

        private async Task PlayViaMediaFoundationAsync(string url, CancellationToken ct)
        {
            using var reader = new MediaFoundationReader(url);

            SetState(PlaybackState.Playing);
            ConnectedSince = DateTime.Now;
            ReconnectAttempts = 0;

            var buffer = new byte[16384];
            while (!ct.IsCancellationRequested)
            {
                int read = await Task.Run(() => reader.Read(buffer, 0, buffer.Length), ct);
                if (read <= 0) return; // stream ended / connection dropped

                if (CurrentFormat == null)
                {
                    CurrentFormat = new AudioFormatInfo
                    {
                        SampleRate = reader.WaveFormat.SampleRate,
                        Channels = reader.WaveFormat.Channels,
                        BitsPerSample = reader.WaveFormat.BitsPerSample
                    };
                    FormatReady?.Invoke(CurrentFormat);
                    Log?.Invoke($"Formato detectado: {CurrentFormat}");
                }

                BytesDecoded += read;
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                PcmChunkReady?.Invoke(chunk);
            }
        }

        private static bool IsHlsPlaylist(string url) =>
            url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0;

        private void SetState(PlaybackState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose() => Stop();
    }
}
