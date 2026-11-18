; Kiitou setup program (Inno Setup 6).
; Build:  ISCC.exe /DAppVersion=0.9.0 installer\Kiitou.iss      (expects the published files in ..\out)
;
; Installs for the current user only, so neither installing nor updating needs administrator
; rights. Settings and hosts-file backups live in %LOCALAPPDATA%\Kiitou, apart from the program.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6F0C7A52-3B1E-4D8A-9C47-1E5B2A9D4F30}
AppName=Kiitou
AppVersion={#AppVersion}
AppVerName=Kiitou {#AppVersion}
AppPublisher=Krowsingre
AppPublisherURL=https://github.com/Krowsingre/kiitou
AppSupportURL=https://github.com/Krowsingre/kiitou
DefaultDirName={localappdata}\Programs\Kiitou
DisableDirPage=yes
DefaultGroupName=Kiitou
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\out-installer
OutputBaseFilename=Kiitou-Setup-{#AppVersion}
SetupIconFile=..\assets\icons\kiitou_purple.ico
UninstallDisplayIcon={app}\Kiitou.exe
UninstallDisplayName=Kiitou
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\out\Kiitou.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Kiitou"; Filename: "{app}\Kiitou.exe"
Name: "{autodesktop}\Kiitou"; Filename: "{app}\Kiitou.exe"; Tasks: desktopicon

[Run]
; Runs after a normal install (as a ticked "Start Kiitou" box) and after a quiet update.
; A quiet install started with /LAUNCH=0 does not start Kiitou.
Filename: "{app}\Kiitou.exe"; Description: "Start Kiitou"; Flags: nowait postinstall; Check: ShouldLaunch

[Code]
function ShouldLaunch: Boolean;
begin
  Result := ExpandConstant('{param:LAUNCH|1}') <> '0';
end;

// The user chooses, each time Kiitou is uninstalled, whether their settings are removed too.
// A quiet uninstall keeps them.
//
// Before files are removed, an interactive uninstall lets Kiitou offer to take its mappings out
// of the hosts file. (This is done here rather than in [UninstallRun], whose conditions are
// evaluated during Setup, where UninstallSilent cannot be called.)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  ResultCode: Integer;
begin
  if (CurUninstallStep = usUninstall) and (not UninstallSilent) and FileExists(ExpandConstant('{app}\Kiitou.exe')) then
    Exec(ExpandConstant('{app}\Kiitou.exe'), '--uninstall-cleanup', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);

  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\Kiitou');
    if DirExists(DataDir) and (not UninstallSilent) then
    begin
      if MsgBox('Do you also want to delete your Kiitou settings, profiles and hosts-file backups?' + #13#10 + #13#10 +
                'Choose No to keep them for a later reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
