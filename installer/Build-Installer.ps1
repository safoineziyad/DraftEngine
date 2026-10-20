<#
.SYNOPSIS
    Builds the DraftEngine installer (Inno Setup).

.DESCRIPTION
    1. Publishes DraftEngine.App (Release, win-x64, framework-dependent) to .\publish
    2. Locates ISCC.exe (Inno Setup 6)
    3. Compiles installer\DraftEngine.iss
    4. Prints the resulting setup .exe path

    Run from anywhere:
        powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1

.PARAMETER SkipPublish
    Reuse the existing .\publish folder and only recompile the installer.
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

# --- Resolve repo root from this script's location -------------------------
$repoRoot = Split-Path -Parent $PSScriptRoot   # installer\.. = repo root
if (-not (Test-Path (Join-Path $repoRoot 'DraftEngine.sln'))) {
    throw "Could not find DraftEngine.sln in '$repoRoot'. Is this script inside <repo>\installer ?"
}
Set-Location $repoRoot
Write-Host "Repo root: $repoRoot"

function Test-LastExitCode {
    param([string]$Step)
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

# --- 1. Clean + publish -----------------------------------------------------
$publishDir = Join-Path $repoRoot 'publish'
$projectPath = Join-Path $repoRoot 'DraftEngine.App\DraftEngine.App.csproj'

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Write-Host "Cleaning '$publishDir'..."
        Remove-Item -Path $publishDir -Recurse -Force
    }

    Write-Host 'dotnet clean (Release)...'
    & dotnet clean $projectPath -c Release -v quiet --nologo
    Test-LastExitCode 'dotnet clean'

    Write-Host 'dotnet publish DraftEngine.App (Release, win-x64, framework-dependent)...'
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained false -o $publishDir
    Test-LastExitCode 'dotnet publish'

    if (-not (Test-Path (Join-Path $publishDir 'DraftEngine.App.exe'))) {
        throw "Publish succeeded but 'DraftEngine.App.exe' was not found in '$publishDir'."
    }
}
elseif (-not (Test-Path (Join-Path $publishDir 'DraftEngine.App.exe'))) {
    throw "SkipPublish was set but '$publishDir' does not contain DraftEngine.App.exe. Run without -SkipPublish."
}

# --- 2. Locate ISCC.exe (Inno Setup 6 compiler) ----------------------------
$candidates = New-Object System.Collections.Generic.List[string]

$pf86 = ${env:ProgramFiles(x86)}
if ($pf86) { $candidates.Add((Join-Path $pf86 'Inno Setup 6\ISCC.exe')) }
if ($env:ProgramFiles) { $candidates.Add((Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) }
if ($env:LOCALAPPDATA) { $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')) }

# Registry (per-machine and per-user installs record "Inno Setup: App Path")
$uninstallKeys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*_is1',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*_is1',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*_is1'
)
foreach ($key in $uninstallKeys) {
    Get-ItemProperty -Path $key -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like '*Inno Setup*' -and $_.'Inno Setup: App Path' } |
        ForEach-Object { $candidates.Add((Join-Path $_.'Inno Setup: App Path' 'ISCC.exe')) }
}

$iscc = $null
foreach ($path in $candidates) {
    if ($path -and (Test-Path -LiteralPath $path)) {
        $iscc = $path
        break
    }
}
if (-not $iscc) {
    $fromPath = Get-Command -Name 'ISCC.exe', 'iscc' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($fromPath) { $iscc = $fromPath.Source }
}
if (-not $iscc) {
    throw @"
ISCC.exe (Inno Setup 6) was not found.

Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
(supported install locations: Program Files, Program Files (x86), or %LOCALAPPDATA%\Programs),
or add ISCC.exe to your PATH, then run this script again.
"@
}
Write-Host "ISCC: $iscc"

# --- 3. Compile the installer ---------------------------------------------
$issPath = Join-Path $repoRoot 'installer\DraftEngine.iss'
if (-not (Test-Path -LiteralPath $issPath)) { throw "Installer script not found: $issPath" }

Write-Host "Compiling '$issPath'..."
& $iscc $issPath
Test-LastExitCode 'ISCC (Inno Setup compile)'

# --- 4. Report the result ---------------------------------------------------
$version = 'unknown'
$match = Select-String -Path $issPath -Pattern '^\s*#define\s+AppVersion\s+"([^"]+)"' | Select-Object -First 1
if ($match) { $version = $match.Matches[0].Groups[1].Value }

$setupPath = Join-Path $repoRoot ("installer\Output\DraftEngine-Setup-{0}.exe" -f $version)
if (-not (Test-Path -LiteralPath $setupPath)) {
    $newest = Get-ChildItem -Path (Join-Path $repoRoot 'installer\Output') -Filter 'DraftEngine-Setup-*.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) { $setupPath = $newest.FullName }
}

if (Test-Path -LiteralPath $setupPath) {
    $size = (Get-Item -LiteralPath $setupPath).Length
    Write-Host ''
    Write-Host ('Installer created: {0}  ({1:N2} MB)' -f $setupPath, ($size / 1MB)) -ForegroundColor Green
}
else {
    throw 'ISCC reported success but the setup .exe was not found in installer\Output.'
}
