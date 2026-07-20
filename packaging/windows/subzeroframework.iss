; SubZero Framework Edition — Windows installer (Inno Setup 6).
;
; Lays down the exact release layout the app expects (UI at {app}, packaged service helper at
; {app}\service-package\windows\), then registers the service through the SERVICE'S OWN
; `--service-management` CLI — the same code path the in-app Settings/Warnings buttons use — so there is
; one tested registration flow. Uninstall drives the same CLI before file removal.
;
; Compile: iscc /DAppVersion=<ver> /DSourceDir=<staged-artifact-dir> /DArch=<x64|arm64> subzeroframework.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

#define ServiceExe "service-package\windows\SubZeroFramework.Service.exe"

[Setup]
AppId={{7E4B8C21-5A9D-4F3E-9B0A-2C6D1E8F4A57}
AppName=SubZero Framework Edition
AppVersion={#AppVersion}
AppPublisher=TekuSP
AppPublisherURL=https://github.com/TekuSP/SubZeroFramework
AppSupportURL=https://github.com/TekuSP/SubZeroFramework/issues
DefaultDirName={autopf}\SubZeroFramework
DefaultGroupName=SubZero Framework Edition
DisableProgramGroupPage=yes
; Admin is required: the post-install step registers the Windows service.
PrivilegesRequired=admin
OutputBaseFilename=SubZeroFramework-Setup-{#AppVersion}-{#Arch}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
UninstallDisplayIcon={app}\SubZeroFramework.exe
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "installservice"; Description: "Register and start the SubZero background service (required for fan control and telemetry)"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\SubZero Framework Edition"; Filename: "{app}\SubZeroFramework.exe"
Name: "{autodesktop}\SubZero Framework Edition"; Filename: "{app}\SubZeroFramework.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SubZeroFramework.exe"; Description: "Launch SubZero"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stops and deregisters the SCM entry before files are removed (RunOnceId keeps it single-shot).
Filename: "{app}\{#ServiceExe}"; Parameters: "--service-management uninstall"; RunOnceId: "UninstallSubZeroService"; Flags: runhidden waituntilterminated

[Code]
const
  ServiceName = 'SubZeroFrameworkService';

function ServiceInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query ' + ServiceName, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure RunServiceManagement(const Operation: String);
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\{#ServiceExe}'), '--service-management ' + Operation, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log(Format('Service management "%s" exited with %d.', [Operation, ResultCode]));
end;

// Reinstall-over-the-top is the documented update strategy, and [Files] overwrites
// service-package\windows\SubZeroFramework.Service.exe — which is the SCM binPath target and is held open
// by the SCM whenever the service runs. A previous install sets start=auto and restarts it, so on every
// update the file IS locked; without stopping first the copy fails or is silently skipped, leaving the old
// binary registered. Uninstall already deregisters before removing files ([UninstallRun]); this closes the
// matching gap on install. First installs no-op because ServiceInstalled() is false.
procedure StopServiceBeforeOverwrite();
var
  ResultCode: Integer;
begin
  if not ServiceInstalled() then
    exit;

  // A non-zero exit just means it was already stopped, which is fine — we only need the file unlocked.
  Exec(ExpandConstant('{sys}\net.exe'), 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log(Format('Pre-copy "net stop %s" exited with %d.', [ServiceName, ResultCode]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // ssInstall fires BEFORE any file is copied.
  if CurStep = ssInstall then
    StopServiceBeforeOverwrite();

  // ssPostInstall fires after files land and before the [Run] entry launches the UI, so the service is
  // always registered and started before the app first connects to it.
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('installservice') then
  begin
    // Fresh install registers; re-running the installer over an existing service re-points binPath
    // and refreshes the restart-on-failure configuration instead.
    if ServiceInstalled() then
      RunServiceManagement('update')
    else
      RunServiceManagement('install');

    RunServiceManagement('enable-autorun');
    RunServiceManagement('restart');

    if not ServiceInstalled() then
      MsgBox('The SubZero background service could not be registered. You can install it later from ' +
        'Settings -> Service inside the app (it will prompt for administrator).', mbInformation, MB_OK);
  end;
end;
