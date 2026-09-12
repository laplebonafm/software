# Protocolo de VirtualStreamPlayer (named pipe)

Este documento describe cómo cualquier programa (en cualquier lenguaje que
pueda abrir un named pipe de Windows) puede leer el audio en vivo que expone
VirtualStreamPlayer, **sin necesidad de ningún dispositivo de audio virtual o
físico**.

## 1. Conexión

- Nombre del pipe por defecto: `VirtualStreamPlayer_Audio`
- Ruta completa: `\\.\pipe\VirtualStreamPlayer_Audio`
- Dirección: solo lectura para el consumidor (el servidor escribe, el
  consumidor lee)
- Se pueden conectar varios consumidores a la vez; cada uno recibe su propia
  copia del audio en vivo desde el momento en que se conecta (más un pequeño
  "pre-roll" de ~2 segundos para que el audio arranque de inmediato).

## 2. Header (12 bytes, una sola vez al conectar)

| Offset | Tamaño | Contenido                                  |
|--------|--------|---------------------------------------------|
| 0      | 4      | ASCII `"VSP1"` (identificador de protocolo) |
| 4      | 4      | int32 little-endian: sample rate (Hz)       |
| 8      | 2      | int16 little-endian: número de canales      |
| 10     | 2      | int16 little-endian: bits por muestra       |

## 3. Cuerpo (continuo, hasta que se desconecte)

Después del header, el pipe entrega un flujo continuo de PCM crudo,
intercalado por canal (interleaved), sin ningún encabezado adicional ni
separadores entre "paquetes" — se debe tratar como un flujo continuo de
bytes, igual que si fuera un archivo WAV sin el header RIFF.

Ejemplo típico: 44100 Hz, 2 canales, 16 bits → cada muestra estéreo ocupa
4 bytes (2 bytes canal izquierdo + 2 bytes canal derecho, con signo, little
endian).

## 4. Reconexión / cortes de la emisora original

Si la URL de streaming original se cae, VirtualStreamPlayer reconecta
automáticamente con backoff exponencial (1s, 2s, 4s... hasta un máximo
configurable). Durante ese lapso el pipe simplemente no entrega bytes nuevos
(no cierra la conexión); el consumidor solo debe tolerar una pausa temporal
en los datos. El formato (sample rate/canales/bits) no cambia mientras dure
la sesión salvo que la emisora en sí cambie de formato.

## 5. Ejemplo mínimo en Python

```python
import struct
import win32pipe, win32file  # pywin32

handle = win32file.CreateFile(
    r"\\.\pipe\VirtualStreamPlayer_Audio",
    win32file.GENERIC_READ, 0, None,
    win32file.OPEN_EXISTING, 0, None)

header = win32file.ReadFile(handle, 12)[1]
magic, sample_rate, channels, bits = struct.unpack("<4siHH", header)
assert magic == b"VSP1"
print(sample_rate, channels, bits)

while True:
    _, data = win32file.ReadFile(handle, 8192)
    if not data:
        break
    # data son bytes PCM crudos listos para reproducir o procesar
```

## 6. API HTTP local (complementaria, no reemplaza al pipe)

Disponible en `http://127.0.0.1:8770/` (puerto configurable):

- `GET /api/status` — estado de la conexión, URL actual, bytes decodificados,
  intentos de reconexión, clientes conectados al pipe
- `GET /api/metadata` — título actual (`StreamTitle` de ICY, si la emisora lo
  envía)
- `GET /api/format` — sample rate / canales / bits una vez detectado
- `GET /api/pipe` — nombre y ruta completa del pipe activo
- `POST /api/connect` con body `{"url": "https://..."}` — cambia de emisora
- `POST /api/disconnect` — detiene la reproducción

Esta API es útil para que un programa consumidor descubra el pipe/formato
por código en vez de tenerlo hardcodeado, o para monitoreo/dashboards.
