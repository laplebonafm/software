# VirtualStreamPlayer

Reproductor de URLs de streaming (radio por Internet, formato MP3/ICY) para
Windows que **no depende de ningún dispositivo de audio virtual** (tipo
VB-Audio Virtual Cable) ni de una tarjeta de sonido física.

En vez de reproducir a un dispositivo, decodifica el stream a PCM en memoria
y expone ese flujo de datos por un **named pipe** (más una API HTTP local de
apoyo), para que cualquier otro programa se conecte directamente al flujo de
datos y lo "detecte" sin pasar por el subsistema de audio de Windows.

## Componentes

- **VirtualStreamPlayer** (WPF) — la app principal: conecta a la URL,
  decodifica MP3→PCM cuadro por cuadro, reconecta automáticamente si se cae
  la emisora, y sirve el PCM por el named pipe + la API local.
- **VirtualStreamPlayer.TestPipeClient** (consola) — herramienta de
  verificación: se conecta al pipe y reproduce el audio por un dispositivo
  real (o lo graba a WAV), solo para confirmar que la tubería funciona.

## Arquitectura

```
URL streaming (HTTP/ICY)
        │
        ▼
   StreamClient  ── strip de metadata ICY inline ──▶ IcyMetadataStream
        │
        ▼
  Mp3StreamDecoder (NAudio, cuadro a cuadro) ──▶ PCM crudo
        │
        ▼
   PlaybackEngine  (reconexión automática con backoff exponencial)
        │
        ├──▶ NamedPipeAudioServer  \\.\pipe\VirtualStreamPlayer_Audio
        │        (multi-cliente, con pre-roll de ~2s por cliente nuevo)
        │
        └──▶ LocalApiServer   http://127.0.0.1:8770/api/*
                 (status, metadata, formato, connect/disconnect)
```

Ver [`docs/PROTOCOL.md`](docs/PROTOCOL.md) para el protocolo exacto del pipe
(header + PCM) si vas a escribir tu propio consumidor.

## Formatos soportados

- **MP3** directo (Shoutcast/Icecast, con o sin `icy-metaint`)
- **AAC / AAC+** directo (mismo mecanismo)
- **HLS (.m3u8)** — la URL debe apuntar al playlist `.m3u8`; Windows Media
  Foundation se encarga de bajar los segmentos

La detección de formato es automática: no hay que indicarle a la app qué
tipo de stream es. Internamente, todo pasa por Windows Media Foundation
(soporta MP3/AAC/HLS de forma nativa desde Windows 8.1); si la emisora usa
metadata ICY inline (típico de radios MP3/AAC "de toda la vida"), esa
metadata se limpia primero y se re-sirve en loopback local antes de
entregársela a Media Foundation — ver `docs/PROTOCOL.md` y los comentarios
en `Streaming/PlaybackEngine.cs` para el detalle exacto.

## Opción para generar el .exe sin usar tu propia PC Windows

Si no quieres instalar el SDK de .NET ni Inno Setup localmente, el proyecto
incluye `.github/workflows/build.yml`: un workflow de GitHub Actions que
compila todo en un runner Windows real en la nube (gratis) y deja el
`.exe`/instalador listo para descargar desde la pestaña **Actions** del
repositorio, sin tocar nada en tu computadora. Solo necesitas:
1. Crear un repo en GitHub (puede ser privado) y subir este proyecto.
2. Esperar 2-3 minutos a que corra el workflow.
3. Descargar el artifact `VirtualStreamPlayerSetup` desde el run.

## Requisitos para compilar (localmente)

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 (opcional, o solo `dotnet` CLI)

Este entorno de desarrollo no tiene el SDK de .NET disponible (es Linux sin
`dotnet`), así que el proyecto se entrega como código fuente completo +
scripts de build, listos para compilar/instalar en tu máquina Windows.

## Compilar y generar el instalador

1. Abre PowerShell en la carpeta del proyecto.
2. Ejecuta:
   ```powershell
   .\build\publish.ps1
   ```
   Esto genera un ejecutable self-contained en `publish\VirtualStreamPlayer\`.
3. Instala [Inno Setup](https://jrsoftware.org/isinfo.php) (gratis) si no lo
   tienes, y compila `installer\VirtualStreamPlayerSetup.iss` (clic derecho →
   "Compile", o desde la app de Inno Setup). Esto genera
   `installer\Output\VirtualStreamPlayerSetup.exe`, listo para instalar en
   cualquier PC Windows.

## Uso rápido

1. Abre VirtualStreamPlayer, pega la URL del stream (ej.
   `https://cast.zuperdns.net/8006/stream`) y presiona **Conectar**.
2. La ventana muestra el named pipe activo (`\\.\pipe\VirtualStreamPlayer_Audio`)
   y la API local (`http://127.0.0.1:8770`).
3. Tu otro programa se conecta a ese pipe (ver `docs/PROTOCOL.md`) y empieza
   a recibir PCM en cuanto conecta — no hace falta seleccionar ningún
   dispositivo de audio en ningún lado.
4. Para verificar que todo funciona sin escribir tu propio consumidor todavía,
   corre `TestPipeClient.exe` (compilado junto con la solución): se conecta
   al mismo pipe y reproduce el audio por tus parlantes reales, o guárdalo a
   WAV con `TestPipeClient.exe --save prueba.wav`.

## Configuración

`%APPDATA%\VirtualStreamPlayer\config.json`:

```json
{
  "StreamUrl": "https://cast.zuperdns.net/8006/stream",
  "AutoReconnect": true,
  "ConnectOnStartup": false,
  "PipeName": "VirtualStreamPlayer_Audio",
  "ApiPort": 8770,
  "MaxReconnectDelaySeconds": 30
}
```

Logs en `%APPDATA%\VirtualStreamPlayer\logs\log-yyyyMMdd.txt`.

## Qué se investigó antes de construirlo

Se buscaron proyectos similares (drivers de audio virtual como
Virtual-Audio-Driver/VB-Cable, proxies de streams tipo Icecast relay,
software de automatización de radio). Todos los que existen o bien crean un
dispositivo de audio virtual (siguen necesitando que el consumidor lo
seleccione como entrada de audio) o simplemente re-transmiten el stream
comprimido (MP3) por HTTP, sin exponer PCM crudo por un canal local tipo
pipe. No se encontró una herramienta existente que combine "sin dispositivo
de audio" + "PCM listo para consumir por pipe/API local" — por eso este
proyecto se construyó desde cero.
