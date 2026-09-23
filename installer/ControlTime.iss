; Instalador de ControlTime (Inno Setup 6)
; Compilacion normal: pwsh build/publish.ps1
; (pasa /DMyAppVersion=<version> y /DPublishDir=<carpeta de publish>)

#define MyAppName "ControlTime"
#define MyAppVersionFallback "1.0.0"
#define ServiceName "ControlTimeService"

#ifndef MyAppVersion
  #define MyAppVersion MyAppVersionFallback
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{D7F4A9E3-6C21-4B58-9E40-7A2D5C1B8F36}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
OutputDir=..\dist
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\ControlTime.App.exe
SetupIconFile=..\src\ControlTime.App\Assets\controltime.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PublishDir}\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\ControlTime.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ControlTime.App.exe"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\Service\ControlTime.Service.exe"" start= delayed-auto obj= LocalSystem DisplayName= ""ControlTime Service"""; StatusMsg: "Registrando el servicio de ControlTime..."; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000"; StatusMsg: "Configurando recuperacion ante fallos..."; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; StatusMsg: "Iniciando el servicio de ControlTime..."; Flags: runhidden
Filename: "{app}\ControlTime.App.exe"; Description: "Iniciar {#MyAppName}"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; RunOnceId: "StopControlTimeService"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; RunOnceId: "DeleteControlTimeService"; Flags: runhidden

[Code]
const
  SvcName = 'ControlTimeService';
  SvcRegKey = 'SYSTEM\CurrentControlSet\Services\ControlTimeService';

var
  DeleteDataRequested: Boolean;

procedure InitializeUninstallProgressForm;
begin
  DeleteDataRequested := False;
  if MsgBox('Desea eliminar tambien los datos de ControlTime (reglas, uso, ' +
    'historial, password de administrador y registros) de ProgramData?',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DeleteDataRequested := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteDataRequested then
  begin
    Log('Eliminando datos de ProgramData por eleccion del usuario...');
    if not DelTree(ExpandConstant('{commonappdata}\ControlTime'), True, True, True) then
      Log('No se pudo eliminar por completo {commonappdata}\ControlTime.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if RegKeyExists(HKLM, SvcRegKey) then
  begin
    Log('Actualizacion detectada: deteniendo ' + SvcName + '...');
    if not Exec(ExpandConstant('{sys}\sc.exe'), 'stop ' + SvcName, '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then
      Log('No se pudo ejecutar sc stop (codigo ' + IntToStr(ResultCode) + ').');
  end;

  ResultCode := 0;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ControlTime.App.exe', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
    Log('ControlTime.App.exe no estaba en ejecucion o ya finalizo (codigo ' + IntToStr(ResultCode) + ').');

  Result := '';
end;
