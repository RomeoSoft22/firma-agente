; Instalador del Agente de Firma Digital (Inno Setup 6).
; Se compila con instalador\construir.ps1, que primero publica el .exe autocontenido.
; Instala por usuario (sin pedir administrador), arranca al iniciar sesión y se puede desinstalar
; desde "Aplicaciones instaladas" de Windows.

#define Nombre "Agente de Firma Digital"
#define Version "1.1.0"
#define Ejecutable "AgenteFirma.exe"
#ifndef Publicacion
  #define Publicacion "..\publish"
#endif

[Setup]
AppId={{8F3E2A51-6C1D-4B7A-9E0F-2D5C3B9A1E77}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher=JuarezGonzales
AppPublisherURL=https://firma.juarezcampos.net
AppSupportURL=https://firma.juarezcampos.net
DefaultDirName={localappdata}\Programs\AgenteFirma
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=salida
OutputBaseFilename=AgenteFirmaSetup
SetupIconFile=..\firma.ico
UninstallDisplayIcon={app}\{#Ejecutable}
UninstallDisplayName={#Nombre}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#Publicacion}\{#Ejecutable}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Publicacion}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Menú Inicio y arranque automático al iniciar sesión en Windows.
Name: "{userprograms}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"
Name: "{userstartup}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"

[Run]
Filename: "{app}\{#Ejecutable}"; Description: "Iniciar el Agente de Firma ahora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Registros de actividad (%LOCALAPPDATA%\AgenteFirma\logs).
Type: filesandordirs; Name: "{localappdata}\AgenteFirma"

[Code]
procedure CerrarAgente();
var
  Codigo: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#Ejecutable}', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;

{ Al actualizar: detener la versión en ejecución para poder reemplazar el .exe. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CerrarAgente();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CerrarAgente();
end;
