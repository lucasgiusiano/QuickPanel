; Inno Setup script - QuickPanel / SidePanel for Edge
; Instalador self-contained (no requiere .NET instalado).
; Compilar con: ISCC.exe installer.iss

#define MyAppName "SidePanel for Edge"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Lucas Giusiano"
#define MyAppExeName "QuickPanel.exe"
; Carpeta generada por: dotnet publish -c Release -r win-x64 --self-contained true -o publish
#define PublishDir "publish"

[Setup]
; AppId identifica la app de forma unica (para updates/desinstalacion). No cambiar entre versiones.
AppId={{B7E4B1A2-9C3D-4E5F-8A1B-0F1E2D3C4B5A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\QuickPanel
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Instala sin pedir permisos de administrador (per-user).
PrivilegesRequired=lowest
OutputBaseFilename=QuickPanelSetup
OutputDir=installer-output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "startup"; Description: "Iniciar {#MyAppName} con Windows"; GroupDescription: "Inicio:"

[Files]
; Empaqueta TODA la carpeta publish (self-contained: incluye el runtime de .NET).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Inicio automatico con Windows (solo si el usuario marco la tarea "startup").
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "QuickPanel"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
; Ofrece ejecutar la app al terminar la instalacion.
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Limpia datos de la app al desinstalar (perfiles WebView2, settings).
Type: filesandordirs; Name: "{userappdata}\QuickPanel"
