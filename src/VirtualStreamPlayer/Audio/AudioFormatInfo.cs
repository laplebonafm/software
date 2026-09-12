namespace VirtualStreamPlayer.Audio
{
    /// <summary>
    /// Describes the raw PCM format that VirtualStreamPlayer exposes to consumers
    /// through the named pipe. This is negotiated once, from the first decoded
    /// MP3 frame of the stream, and stays fixed for the lifetime of the connection.
    /// </summary>
    public class AudioFormatInfo
    {
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 2;
        public int BitsPerSample { get; set; } = 16;

        public int BlockAlign => Channels * (BitsPerSample / 8);

        public override string ToString() =>
            $"{SampleRate} Hz, {Channels}ch, {BitsPerSample}-bit PCM";
    }
}
