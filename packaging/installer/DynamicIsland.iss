; Per-user installer for Dynamic Island (P06-02, ADR 0031). Built by tools/Publish-Layout.ps1 -Installer:
;   ISCC /DAppVersion=<semver> /DLayoutDir=<artifacts\layout> /DOutputDir=<artifacts> DynamicIsland.iss
; Installs the release layout (ADR 0030) to %LOCALAPPDATA%\Programs\DynamicIsland without admin rights.
; Command line: the usual Inno switches (/SILENT, /VERYSILENT, /DIR=...) plus /MANAGED=1, which turns self-update off
; for this copy (IT-managed). Settings, notes and logs live in %LOCALAPPDATA%\DynamicIsland and are kept on uninstall
; unless the user explicitly chooses to delete them.

#ifndef AppVersion
  #error Pass /DAppVersion=<semver>
#endif
#ifndef LayoutDir
  #error Pass /DLayoutDir=<path to the release layout>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{8F2E6B1C-3A47-4C2D-9B6E-5D1F0A7C4E21}
AppName=Dynamic Island
AppVersion={#AppVersion}
AppVerName=Dynamic Island {#AppVersion}
AppPublisher=Dynamic Island for Windows contributors
AppPublisherURL=https://github.com/imamulkadir/DyanamicEye
AppSupportURL=https://github.com/imamulkadir/DyanamicEye/issues
; No elevation ever: {autopf} is %LOCALAPPDATA%\Programs for a per-user install; nothing is written to HKLM.
PrivilegesRequired=lowest
DefaultDirName={autopf}\DynamicIsland
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=DynamicIsland-Setup-win-x64
SetupIconFile=..\..\assets\DynamicIsland.ico
UninstallDisplayIcon={app}\DynamicIsland.exe
UninstallDisplayName=Dynamic Island
LicenseFile={#LayoutDir}\LICENSE.txt
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close the running island before files are replaced or removed (Restart Manager; the app exits on WM_CLOSE).
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; A reinstall or upgrade from this installer replaces the payload; older app-* folders are removed (in-app updates
; keep the previous version for rollback instead, P06-06).
Type: filesandordirs; Name: "{app}\app-*"

[Files]
Source: "{#LayoutDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Dynamic Island"; Filename: "{app}\DynamicIsland.exe"
Name: "{autodesktop}\Dynamic Island"; Filename: "{app}\DynamicIsland.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DynamicIsland.exe"; Description: "{cm:LaunchProgram,Dynamic Island}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\managed.json"
; In-app updates (ADR 0035) add payloads and rewrite current.json that the installer did not place itself.
Type: filesandordirs; Name: "{app}\app-*"
Type: files; Name: "{app}\current.json"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'DynamicIsland';
  TrayWindowTitle = 'DynamicIsland.Tray';
  WM_CLOSE = $0010;

{ Asks a running Dynamic Island to exit like tray > Exit (WM_CLOSE on its tray window), then waits for it. The
  uninstaller does not close apps by itself, and the launcher has no window for Restart Manager to close. }
procedure CloseRunningIsland();
var
  I: Integer;
begin
  if FindWindowByWindowName(TrayWindowTitle) = 0 then
    Exit;
  PostMessage(FindWindowByWindowName(TrayWindowTitle), WM_CLOSE, 0, 0);
  for I := 1 to 75 do
  begin
    Sleep(200);
    if FindWindowByWindowName(TrayWindowTitle) = 0 then
      Break;
  end;
  Sleep(1500); { The launcher exits right after the app. }
end;

{ Windows 10 is labelled honestly (ADR 0001, P07-02): Windows 11 is the tested target; Windows 10 Enterprise/IoT 21H2
  is best effort; Windows 10 Home/Pro is not supported by .NET 10. The user decides; IT installs with
  /SUPPRESSMSGBOXES continue (default Yes). }
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  Result := True;
  GetWindowsVersionEx(Version);
  if Version.Build < 22000 then
    Result := SuppressibleMsgBox(
      'Dynamic Island is made and tested for Windows 11. This PC runs Windows 10 (build ' + IntToStr(Version.Build) + ').' + #13#10#13#10 +
      'Windows 10 Enterprise or IoT Enterprise 21H2 may work, but it is not tested (best effort). ' +
      'Windows 10 Home and Pro are not supported: .NET 10, which Dynamic Island is built on, does not support them.' + #13#10#13#10 +
      'Install anyway?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDYES) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningIsland();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningIsland();
  Result := True;
end;

{ /MANAGED=1 (IT): this copy never checks for or installs updates (ADR 0031). }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (ExpandConstant('{param:MANAGED|0}') = '1') then
    SaveStringToFile(ExpandConstant('{app}\managed.json'), '{ "disableSelfUpdate": true }', False);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Our sign-in entry (written by the app when the user chose it) must not outlive the program. }
    if RegQueryStringValue(HKCU, RunKey, RunValue, Command) and
       (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Command)) > 0) then
      RegDeleteValue(HKCU, RunKey, RunValue);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { User data is kept unless explicitly requested; a silent uninstall never deletes it. }
    DataDir := ExpandConstant('{localappdata}\DynamicIsland');
    if (not UninstallSilent) and DirExists(DataDir) and
       (MsgBox('Also delete your Dynamic Island settings, notes, tasks and logs?' + #13#10#13#10 +
               'They are in ' + DataDir + '. Choose No to keep them for a later reinstall.',
               mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(DataDir, True, True, True);
  end;
end;
