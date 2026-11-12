[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'DraftEngine.App\DraftEngine.App.csproj'
$issPath = Join-Path $repoRoot 'installer\DraftEngine.iss'
$publishDirectory = Join-Path $repoRoot 'publish'
$distDirectory = Join-Path $repoRoot 'installer\Output'

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'DraftEngine.sln'))) {
    throw "Repository root was not found at '$repoRoot'."
}

$versionMatch = Select-String -Path $issPath -Pattern '^\s*#define\s+AppVersion\s+"([^"]+)"' |
    Select-Object -First 1
if (-not $versionMatch) {
    throw "AppVersion was not found in '$issPath'."
}
$installerVersion = $versionMatch.Matches[0].Groups[1].Value
if ($installerVersion -ne '0.4.0.1') {
    throw "Expected installer AppVersion 0.4.0.1 but found '$installerVersion'."
}

$projectText = Get-Content -LiteralPath $projectPath -Raw
if ($projectText -notmatch '<Version>0\.4\.0-beta\.1</Version>' -or
    $projectText -notmatch '<AssemblyVersion>0\.4\.0\.1</AssemblyVersion>' -or
    $projectText -notmatch '<FileVersion>0\.4\.0\.1</FileVersion>') {
    throw 'The project package, assembly, and file versions do not match the local beta installer.'
}

$expectedAppId = '{{48B229B2-F5ED-41C4-A8B4-33F8C0953745}'
$issText = Get-Content -LiteralPath $issPath -Raw
if ($issText -notmatch [regex]::Escape("#define AppId `"$expectedAppId`"")) {
    throw 'Installer AppId differs from the v0.2.2 AppId; refusing to build a non-upgrading installer.'
}
if ($issText -match '(?i)DelTree|DeleteUserData|userappdata') {
    throw 'The installer contains a user-data deletion path; refusing to build.'
}

function Assert-ExitCode {
    param([string]$Step)
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

function Find-InnoSetupCompiler {
    $paths = [System.Collections.Generic.List[string]]::new()
    if (${env:ProgramFiles(x86)}) {
        $paths.Add((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'))
    }
    if ($env:ProgramFiles) {
        $paths.Add((Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
    }
    if ($env:LOCALAPPDATA) {
        $paths.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    }

    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        return $command.Source
    }

    throw 'Inno Setup 6 ISCC.exe was not found. Install Inno Setup 6 or add ISCC.exe to PATH.'
}

Set-Location $repoRoot
$commitHash = (& git rev-parse HEAD).Trim()
Assert-ExitCode 'git rev-parse'
$buildDateUtc = [DateTime]::UtcNow.ToString(
    "yyyy-MM-dd'T'HH:mm:ss'Z'",
    [Globalization.CultureInfo]::InvariantCulture)

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null
Write-Host "Installer output: $distDirectory"

$setupPath = Join-Path $distDirectory "DraftEngine-Setup-$installerVersion.exe"
$checksumPath = "$setupPath.sha256"
foreach ($path in @($setupPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

Write-Host 'Cleaning the Release app output...'
& dotnet clean $projectPath -c Release --nologo -v quiet
Assert-ExitCode 'dotnet clean'

Write-Host "Publishing DraftEngine $installerVersion for win-x64..."
& dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
    -o $publishDirectory `
    "-p:BuildCommitHash=$commitHash" `
    "-p:BuildDateUtc=$buildDateUtc"
Assert-ExitCode 'dotnet publish'

if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'DraftEngine.App.exe'))) {
    throw "The published application executable was not found in '$publishDirectory'."
}

$signCertificate = $env:DRAFTENGINE_SIGN_CERT
$signPassword = $env:DRAFTENGINE_SIGN_PASSWORD
if ([string]::IsNullOrWhiteSpace($signCertificate) -or [string]::IsNullOrWhiteSpace($signPassword)) {
    Write-Host 'Signing skipped: both DRAFTENGINE_SIGN_CERT and DRAFTENGINE_SIGN_PASSWORD are required.'
}
else {
    $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $signTool) {
        throw 'Signing was requested, but signtool.exe was not found.'
    }
    & $signTool.Source sign /f $signCertificate /p $signPassword /fd sha256 `
        /tr http://timestamp.digicert.com /td sha256 (Join-Path $publishDirectory 'DraftEngine.App.exe')
    Assert-ExitCode 'signtool'
}

$compiler = Find-InnoSetupCompiler
Write-Host "Compiling installer with '$compiler'..."
& $compiler "/O$distDirectory" "/FDraftEngine-Setup-$installerVersion" $issPath
Assert-ExitCode 'Inno Setup compiler'

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Inno Setup completed without creating '$setupPath'."
}

$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path -Leaf $setupPath)" -Encoding ascii

Write-Host ''
Write-Host 'Created paths:'
Write-Host $publishDirectory
Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force |
    ForEach-Object { Write-Host $_.FullName }
Write-Host $distDirectory
Write-Host $setupPath
Write-Host $checksumPath
Write-Host "SHA-256: $hash"
