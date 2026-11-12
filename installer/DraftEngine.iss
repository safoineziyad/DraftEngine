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
#define AppVersion "0.4.0.1"
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
RestartApplications=yes

; --- Optional Authenticode signing -------------------------------------------
; Set DRAFTENGINE_SIGN_CERT (path to a .pfx) and DRAFTENGINE_SIGN_PASSWORD before
; compiling to sign the installer and the uninstaller. When the variable is absent
; the directives are omitted and the build proceeds unsigned - no failure.
#define SignCert GetEnv("DRAFTENGINE_SIGN_CERT")
#define SignPassword GetEnv("DRAFTENGINE_SIGN_PASSWORD")
#if SignCert != "" && SignPassword != ""
SignTool=signtool sign /f "{#SignCert}" /p "{#SignPassword}" /fd sha256 /tr http://timestamp.digicert.com /td sha256 $f
SignedUninstaller=yes
#endif

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "app"; Description: "DraftEngine application"; Types: full compact custom; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; The published output (..\publish). ignoreversion so every build overwrites.
; Debug symbols are not shipped to users.
[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app

[Icons]
Name: "{group}\DraftEngine"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Comment: "AI contract drafting assistant"
Name: "{autodesktop}\DraftEngine"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Comment: "AI contract drafting assistant"; Tasks: desktopicon

[Run]
Filename: "{app}\DraftEngine.App.exe"; Flags: nowait postinstall skipifsilent
; The interactive entry skips silent mode; this entry relaunches after a silent update.
Filename: "{app}\DraftEngine.App.exe"; Flags: nowait; Check: WizardSilent

[Code]
const
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

{ The app is published framework-dependent, so the x64 .NET 8 Desktop Runtime must be
  present. Check the shared framework folder; a missing runtime stops setup with a clear
  message and a download link instead of failing later when the app cannot start. }
function DesktopRuntime8Installed(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsWin64 then
    Exit;

  if DesktopRuntime8Installed() then
    Exit;

  if MsgBox('DraftEngine needs the Microsoft .NET 8 Desktop Runtime (x64), which is not installed.' + #13#10#13#10 +
            'Download it from:' + #13#10 + DotNetDownloadUrl + #13#10#13#10 +
            'Open the download page now?', mbCriticalError, MB_YESNO) = IDYES then
  begin
    ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;

  Result := False;
end;
