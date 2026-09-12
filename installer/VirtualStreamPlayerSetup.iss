; Script de Inno Setup para VirtualStreamPlayer.
; Requiere haber corrido build\publish.ps1 antes (genera publish\VirtualStreamPlayer\).
; Compilar con Inno Setup (https://jrsoftware.org/isinfo.php):
;   Abrir este archivo -> Build -> Compile
; Genera: installer\Output\VirtualStreamPlayerSetup.exe

#define MyAppName "VirtualStreamPlayer"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "La Plebona 100.9 FM"
#define MyAppExeName "VirtualStreamPlayer.exe"

[Setup]
AppId={{B7F1D9A2-4C3E-4A5B-9F1D-VSP100900001}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=VirtualStreamPlayerSetup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el Escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\publish\VirtualStreamPlayer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\TestPipeClient (verificación)"; Filename: "{app}\TestPipeClient.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent
