using System;
using System.IO;
using System.Text;

namespace VirtualStreamPlayer.Streaming
{
    /// <summary>
    /// Wraps the raw HTTP response stream from an Icecast/SHOUTcast server and strips
    /// out the inline metadata blocks that are interleaved every "icy-metaint" bytes,
    /// so that whatever reads from this stream only ever sees clean audio bytes
    /// (MP3 frames). Raises <see cref="MetadataChanged"/> whenever the station sends
    /// a non-empty "StreamTitle" update.
    /// </summary>
    public class IcyMetadataStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _metaInt; // 0 = server does not send inline metadata
        private int _bytesUntilMeta;

        public event Action<string>? MetadataChanged;

        public IcyMetadataStream(Stream inner, int metaInt)
        {
            _inner = inner;
            _metaInt = metaInt;
            _bytesUntilMeta = metaInt;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_metaInt <= 0)
            {
                // No inline metadata negotiated; pass through untouched.
                return _inner.Read(buffer, offset, count);
            }

            // Never read past the next metadata marker in a single call, so the
            // caller always gets a clean run of audio bytes.
            int toRead = Math.Min(count, _bytesUntilMeta);
            if (toRead == 0)
            {
                ConsumeMetadataBlock();
                toRead = Math.Min(count, _metaInt);
            }

            int read = _inner.Read(buffer, offset, toRead);
            if (read <= 0) return read;

            _bytesUntilMeta -= read;
            return read;
        }

        private void ConsumeMetadataBlock()
        {
            int lengthByte = _inner.ReadByte();
            if (lengthByte < 0)
                throw new EndOfStreamException("Stream cerrado durante bloque de metadata ICY");

            int metaLength = lengthByte * 16;
            _bytesUntilMeta = _metaInt;

            if (metaLength == 0) return;

            var metaBuffer = new byte[metaLength];
            int read = 0;
            while (read < metaLength)
            {
                int n = _inner.Read(metaBuffer, read, metaLength - read);
                if (n <= 0) throw new EndOfStreamException("Stream cerrado leyendo metadata ICY");
                read += n;
            }

            var text = Encoding.UTF8.GetString(metaBuffer).TrimEnd('\0');
            var title = ExtractStreamTitle(text);
            if (!string.IsNullOrEmpty(title))
                MetadataChanged?.Invoke(title);
        }

        private static string? ExtractStreamTitle(string metaText)
        {
            // Format looks like: StreamTitle='Artist - Track';StreamUrl='...';
            const string marker = "StreamTitle='";
            int start = metaText.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            int end = metaText.IndexOf("';", start, StringComparison.Ordinal);
            if (end < 0) return null;
            return metaText.Substring(start, end - start);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
