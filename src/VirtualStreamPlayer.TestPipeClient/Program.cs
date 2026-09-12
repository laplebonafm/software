using System;
using System.IO;
using System.IO.Pipes;
using NAudio.Wave;

// Herramienta de VERIFICACIÓN, no parte del producto final.
// Se conecta al named pipe que expone VirtualStreamPlayer, lee el header +
// el PCM crudo, y lo reproduce con NAudio (usando un dispositivo real) solo
// para confirmar que el audio que sale del pipe es correcto.
//
// Uso:
//   TestPipeClient.exe                       -> usa el pipe por defecto y reproduce
//   TestPipeClient.exe --pipe MiPipe         -> especifica el nombre del pipe
//   TestPipeClient.exe --save salida.wav     -> en vez de reproducir, graba a WAV

string pipeName = "VirtualStreamPlayer_Audio";
string? saveWavPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--pipe" && i + 1 < args.Length) pipeName = args[++i];
    else if (args[i] == "--save" && i + 1 < args.Length) saveWavPath = args[++i];
}

Console.WriteLine($"Conectando a \\\\.\\pipe\\{pipeName} ...");

using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
pipe.Connect(10000);
Console.WriteLine("Conectado. Leyendo header...");

var header = new byte[12];
ReadExact(pipe, header, 12);

string magic = System.Text.Encoding.ASCII.GetString(header, 0, 4);
if (magic != "VSP1")
{
    Console.WriteLine($"Header inesperado: '{magic}'. ¿Es el pipe correcto?");
    return;
}

int sampleRate = BitConverter.ToInt32(header, 4);
short channels = BitConverter.ToInt16(header, 8);
short bits = BitConverter.ToInt16(header, 10);
Console.WriteLine($"Formato: {sampleRate} Hz, {channels}ch, {bits}-bit");

var waveFormat = new WaveFormat(sampleRate, bits, channels);

if (saveWavPath != null)
{
    Console.WriteLine($"Grabando a {saveWavPath} (Ctrl+C para detener)...");
    using var writer = new WaveFileWriter(saveWavPath, waveFormat);
    var buffer = new byte[8192];
    while (true)
    {
        int read = pipe.Read(buffer, 0, buffer.Length);
        if (read <= 0) break;
        writer.Write(buffer, 0, read);
    }
}
else
{
    Console.WriteLine("Reproduciendo (Ctrl+C para detener)...");
    using var waveOut = new WaveOutEvent();
    var bufferedProvider = new BufferedWaveProvider(waveFormat)
    {
        BufferDuration = TimeSpan.FromSeconds(5),
        DiscardOnBufferOverflow = true
    };
    waveOut.Init(bufferedProvider);
    waveOut.Play();

    var buffer = new byte[8192];
    while (true)
    {
        int read = pipe.Read(buffer, 0, buffer.Length);
        if (read <= 0) break;
        bufferedProvider.AddSamples(buffer, 0, read);
    }
}

static void ReadExact(Stream s, byte[] buffer, int count)
{
    int read = 0;
    while (read < count)
    {
        int n = s.Read(buffer, read, count - read);
        if (n <= 0) throw new EndOfStreamException();
        read += n;
    }
}
