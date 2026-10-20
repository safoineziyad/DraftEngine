; ============================================================================
; DraftEngine - Inno Setup 6 installer script
;
; Consumes the published output of:
;   dotnet publish DraftEngine.App\DraftEngine.App.csproj -c Release -r win-x64 --self-contained false -o publish
;
; Build via installer\Build-Installer.ps1, or directly:
;   ISCC.exe installer\DraftEngine.iss
;
; NOTE: relative paths below are resolved relative to THIS file (installer\),
; so the publish folder is ..\publish and the license is ..\LICENSE.
; ============================================================================

; --- Single source of truth for the version --------------------------------
#define AppVersion "1.0.0"
; ---------------------------------------------------------------------------

#define AppName "DraftEngine"
#define AppPublisher "Safoine Ziyad"
#define AppExeName "DraftEngine.App.exe"
; Stable application GUID - changing this after release would make Windows
; treat a new install as a different application (orphaning the old entry).
#define AppId "{{48B229B2-F5ED-41C4-A8B4-33F8C0953745}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=AI contract drafting assistant
VersionInfoProductName={#AppName}
VersionInfoOriginalFilename=DraftEngine-Setup-{#AppVersion}.exe
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}

; Per-user install (no UAC prompt) - this is a single-user, single-machine tool.
; With PrivilegesRequired=lowest, {autopf} resolves to {localappdata}\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\DraftEngine
DefaultGroupName=DraftEngine
DisableProgramGroupPage=yes

; 64-bit install on x64 (and x64-on-ARM64) Windows 10 1809 or later.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; License shown on the "Select License" wizard page.
LicenseFile=..\LICENSE

; Output: installer\Output\DraftEngine-Setup-<version>.exe
OutputDir=Output
OutputBaseFilename=DraftEngine-Setup-{#AppVersion}

; No SetupIconFile: we do not ship a dedicated .ico, so Inno Setup's
; default installer icon is used. Do not add an .ico without producing one.
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "app"; Description: "DraftEngine application"; Types: full compact custom; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; The published output (..\publish). ignoreversion so every build overwrites.
[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app

[Icons]
Name: "{group}\DraftEngine"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Comment: "AI contract drafting assistant"
Name: "{autodesktop}\DraftEngine"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Comment: "AI contract drafting assistant"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// ---------------------------------------------------------------------------
// User data (contracts, settings, logs) lives in %APPDATA%\DraftEngine and is
// NOT part of the installation, so a normal uninstall never touches it.
// On uninstall we ask once (default: No / keep data) and delete afterwards.
// ---------------------------------------------------------------------------
var
  DeleteUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True; // never block the uninstall itself
  DeleteUserData := False;
  if DirExists(ExpandConstant('{userappdata}\DraftEngine')) then
    DeleteUserData := MsgBox(
      'Do you also want to delete your DraftEngine data (contracts and settings)?' + #13#13 +
      'Location: ' + ExpandConstant('{userappdata}\DraftEngine') + #13#13 +
      'Click No to keep your data (it will be reused by a future installation).',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES; // MB_DEFBUTTON2 = No is default
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
    DelTree(ExpandConstant('{userappdata}\DraftEngine'), True, True, True);
end;
