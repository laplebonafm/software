using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VirtualStreamPlayer.Audio;
using VirtualStreamPlayer.Logging;

namespace VirtualStreamPlayer.Ipc
{
    /// <summary>
    /// Publishes the decoded PCM stream over a named pipe (\\.\pipe\{PipeName}),
    /// so that any other program - written in any language that can open a
    /// Windows named pipe - can read the raw audio directly, without needing a
    /// virtual audio cable, a real sound device, or any OS-level audio API.
    ///
    /// Protocol (see docs/PROTOCOL.md for the full spec):
    ///   1. Consumer connects to the pipe.
    ///   2. Server immediately writes a 12-byte header:
    ///        bytes 0-3  : ASCII magic "VSP1"
    ///        bytes 4-7  : int32 little-endian sample rate (e.g. 44100)
    ///        bytes 8-9  : int16 little-endian channel count
    ///        bytes 10-11: int16 little-endian bits per sample
    ///   3. From then on, the server writes a continuous stream of raw
    ///      interleaved PCM samples until the consumer disconnects or the
    ///      station drops.
    ///
    /// Multiple consumers can connect at the same time; each gets its own
    /// independent copy of the live audio from the moment it connects (plus a
    /// short pre-roll so playback starts instantly instead of with dead air).
    /// </summary>
    public class NamedPipeAudioServer : IDisposable
    {
        private const string Magic = "VSP1";
        private const int PreRollMaxBytes = 44100 * 2 * 2 * 2; // ~2s of 44.1kHz 16-bit stereo

        private readonly string _pipeName;
        private readonly ConcurrentDictionary<Guid, ClientSlot> _clients = new();
        private readonly object _prerollLock = new();
        private readonly Queue<byte[]> _preroll = new();
        private int _prerollBytes;

        private CancellationTokenSource? _cts;
        private AudioFormatInfo? _format;

        public string PipeName => _pipeName;
        public int ConnectedClientCount => _clients.Count;

        public NamedPipeAudioServer(string pipeName)
        {
            _pipeName = pipeName;
        }

        public void SetFormat(AudioFormatInfo format)
        {
            _format = format;
            // Format is only known once the first frame decodes; clear any stale
            // pre-roll captured before it (there shouldn't be any in practice).
            lock (_prerollLock) { _preroll.Clear(); _prerollBytes = 0; }
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            Logger.Info($"Named pipe iniciado: \\\\.\\pipe\\{_pipeName}");
        }

        public void Stop()
        {
            _cts?.Cancel();
            foreach (var slot in _clients.Values) slot.Dispose();
            _clients.Clear();
        }

        /// <summary>Called by PlaybackEngine for every decoded PCM chunk.</summary>
        public void Broadcast(byte[] pcmChunk)
        {
            lock (_prerollLock)
            {
                _preroll.Enqueue(pcmChunk);
                _prerollBytes += pcmChunk.Length;
                while (_prerollBytes > PreRollMaxBytes && _preroll.Count > 0)
                    _prerollBytes -= _preroll.Dequeue().Length;
            }

            foreach (var slot in _clients.Values)
                slot.Enqueue(pcmChunk);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.Out,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error("Error aceptando conexión en el named pipe", ex);
                    pipe?.Dispose();
                    await Task.Delay(500, ct).ContinueWith(_ => { });
                    continue;
                }

                var id = Guid.NewGuid();
                var slot = new ClientSlot(pipe);
                _clients[id] = slot;
                Logger.Info($"Cliente conectado al pipe (total: {_clients.Count})");

                _ = RunClientAsync(id, slot, ct);
            }
        }

        private async Task RunClientAsync(Guid id, ClientSlot slot, CancellationToken ct)
        {
            try
            {
                if (_format != null)
                    await slot.Pipe.WriteAsync(BuildHeader(_format), ct);

                byte[][] prerollSnapshot;
                lock (_prerollLock) prerollSnapshot = _preroll.ToArray();
                foreach (var chunk in prerollSnapshot)
                    await slot.Pipe.WriteAsync(chunk, ct);

                while (!ct.IsCancellationRequested)
                {
                    var chunk = await slot.DequeueAsync(ct);
                    if (chunk == null) continue;
                    await slot.Pipe.WriteAsync(chunk, ct);
                    await slot.Pipe.FlushAsync(ct);
                }
            }
            catch (Exception)
            {
                // Consumer disconnected or pipe broke - not an error, just cleanup.
            }
            finally
            {
                _clients.TryRemove(id, out _);
                slot.Dispose();
                Logger.Info($"Cliente desconectado del pipe (total: {_clients.Count})");
            }
        }

        private static byte[] BuildHeader(AudioFormatInfo format)
        {
            var header = new byte[12];
            Encoding.ASCII.GetBytes(Magic).CopyTo(header, 0);
            BitConverter.GetBytes(format.SampleRate).CopyTo(header, 4);
            BitConverter.GetBytes((short)format.Channels).CopyTo(header, 8);
            BitConverter.GetBytes((short)format.BitsPerSample).CopyTo(header, 10);
            return header;
        }

        public void Dispose() => Stop();

        private class ClientSlot : IDisposable
        {
            public NamedPipeServerStream Pipe { get; }
            private readonly BlockingCollection<byte[]> _queue = new(boundedCapacity: 200);

            public ClientSlot(NamedPipeServerStream pipe) => Pipe = pipe;

            public void Enqueue(byte[] chunk)
            {
                // If a consumer is too slow, drop the oldest chunk rather than
                // blocking the whole broadcast (and every other client with it).
                if (!_queue.TryAdd(chunk))
                {
                    _queue.TryTake(out _);
                    _queue.TryAdd(chunk);
                }
            }

            public async Task<byte[]?> DequeueAsync(CancellationToken ct)
            {
                try
                {
                    return await Task.Run(() => _queue.Take(ct), ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            public void Dispose()
            {
                try { Pipe.Dispose(); } catch { /* ignore */ }
                _queue.Dispose();
            }
        }
    }
}
