; Inno Setup script for NitroCustom. Build with tools\build_installer.ps1.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\dist"
#endif

#define AppName "NitroCustom"
#define ServiceName "NitroCustom"

[Setup]
AppId={{6B0C4E0A-3D8E-4D2B-9A57-2F1D7C5B8E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NitroCustom
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=NitroCustom-Setup-{#AppVersion}
SetupIconFile=..\src\NitroCustom.App\Assets\nitrocustom.ico
UninstallDisplayIcon={app}\NitroCustom.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
CloseApplications=no

[Languages]
; English first: it is the fallback. Inno picks Russian when Windows is displayed in Russian.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.DesktopIcon=Desktop shortcut
english.RegisteringService=Registering the service...
english.StartingService=Starting the service...
english.OpenApp=Open NitroCustom
russian.DesktopIcon=Ярлык на рабочем столе
russian.RegisteringService=Регистрация службы...
russian.StartingService=Запуск службы...
russian.OpenApp=Открыть NitroCustom

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\NitroCustom.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\NitroCustom.exe"; Tasks: desktopicon

[Registry]
; The service starts the tray app at logon; drop the Run entry written by earlier builds.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NitroCustom"; Flags: deletevalue uninsdeletevalue

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\NitroCustom.Service.exe"" start= auto DisplayName= ""NitroCustom"""; Flags: runhidden; StatusMsg: "{cm:RegisteringService}"
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Thermal profiles, keyboard lighting and hotkeys for Acer Nitro without Acer software."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/10000/none/0"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; StatusMsg: "{cm:StartingService}"
; The service starts the agent, the agent starts the app in the tray; this only opens the window on the finish page.
Filename: "{app}\NitroCustom.exe"; Description: "{cm:OpenApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure StopAndRemoveService;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(1500);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(500);
end;

procedure KillApp;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM NitroCustom.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM NitroCustom.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Upgrade over an existing install: release the files first.
  // Service first, otherwise it starts the app again right after the kill.
  StopAndRemoveService;
  KillApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopAndRemoveService;
    KillApp;
  end;
end;
