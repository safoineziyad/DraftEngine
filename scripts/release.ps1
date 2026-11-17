<#
.SYNOPSIS
    Prepares (and optionally publishes) a signed DraftEngine update release.

.DESCRIPTION
    Safety rules:
      * Run with -DryRun first. A non-dry run refuses unless a matching dry-run marker exists.
      * The private signing key is read from $env:DRAFTENGINE_UPDATE_KEY and must live OUTSIDE
        the repository; UpdateKeyTool enforces this. The key is never copied into the project.
      * Nothing is published unless -Publish is passed explicitly. Without it the script only
        writes verified artifacts under dist\<version>\ and prints the gh command.

    Dry run: validates the version/notes/installer, prints every step and writes
    .release\dryrun-<version>.json. Never signs, never uploads.
    Non-dry:  signs installer metadata, verifies the manifest and stages latest.json.

.PARAMETER Version
    Release SemVer, e.g. 0.4.0 or 0.4.0-beta.2.

.PARAMETER NotesFile
    JSON object with localized notes, e.g. {"en":"...","fr":"...","ar":"...","es":"..."}.
    "en" is required; the other languages are optional.

.PARAMETER Prerelease
    Marks the release as a pre-release (beta channel manifest).

.PARAMETER DryRun
    Validate and plan only. Always run this first.

.PARAMETER Publish
    Also run `gh release create` with the staged artifacts. Requires a successful non-dry run
    first. Omit it to keep everything local.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$NotesFile,
    [switch]$Prerelease,
    [switch]$DryRun,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Fail([string]$Message) { throw $Message }

# --- Validate version -------------------------------------------------------
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') {
    Fail "Version '$Version' is not valid SemVer (expected MAJOR.MINOR.PATCH[-prerelease])."
}
if ($Prerelease -and $Version -notmatch '-') {
    Fail '-Prerelease requires a pre-release suffix in -Version (for example 0.4.0-beta.2).'
}
if (-not $Prerelease -and $Version -match '-') {
    Fail "Version '$Version' has a pre-release suffix; pass -Prerelease."
}

# --- Validate notes ---------------------------------------------------------
$notesPath = Resolve-Path -LiteralPath $NotesFile -ErrorAction SilentlyContinue
if (-not $notesPath -or -not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
    Fail "Notes file not found: $NotesFile"
}
$notes = Get-Content -LiteralPath $notesPath -Raw | ConvertFrom-Json
$noteProperties = @($notes.PSObject.Properties.Name)
if ($noteProperties -notcontains 'en' -or [string]::IsNullOrWhiteSpace([string]$notes.en)) {
    Fail 'The notes JSON must contain a non-empty "en" value.'
}

# --- Locate the installer and version source --------------------------------
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propsPath)) { Fail 'Directory.Build.props is missing.' }
$fileVersionMatch = Select-String -Path $propsPath -Pattern '<DraftEngineFileVersion>([^<]+)</DraftEngineFileVersion>' | Select-Object -First 1
if (-not $fileVersionMatch) { Fail 'Directory.Build.props does not define DraftEngineFileVersion.' }
$fileVersion = $fileVersionMatch.Matches[0].Groups[1].Value.Trim()

$installer = Join-Path $repoRoot ("installer\Output\DraftEngine-Setup-{0}.exe" -f $fileVersion)
$installerExists = Test-Path -LiteralPath $installer
$fileName = 'DraftEngine-Setup-{0}.exe' -f $fileVersion

$repoUrl = 'https://github.com/safoineziyad/DraftEngine'
$tag = "v$Version"
$downloadUrl = "$repoUrl/releases/download/$tag/$fileName"

$dryRunDir = Join-Path $repoRoot '.release'
$dryRunMarker = Join-Path $dryRunDir ("dryrun-{0}.json" -f $Version)

# --- Dry run ----------------------------------------------------------------
if ($DryRun) {
    New-Item -ItemType Directory -Force -Path $dryRunDir | Out-Null
    [pscustomobject]@{
        version = $Version
        tag = $tag
        prerelease = [bool]$Prerelease
        notesFile = [string]$notesPath
        installer = $installer
        installerPresent = $installerExists
        preparedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $dryRunMarker -Encoding UTF8

    Write-Host 'DRY RUN - no signing, no build, no upload.'
    Write-Host ("Version:      {0} ({1})" -f $Version, $(if ($Prerelease) { 'prerelease' } else { 'stable' }))
    Write-Host ("Tag:          {0}" -f $tag)
    Write-Host ("Notes:        {0}" -f $notesPath)
    Write-Host ("Installer:    {0}  [{1}]" -f $installer, $(if ($installerExists) { 'found' } else { 'MISSING - build it before the real run' }))
    Write-Host ("Download URL: {0}" -f $downloadUrl)
    Write-Host ('Signing key:  {0}' -f $(if ($env:DRAFTENGINE_UPDATE_KEY) { 'configured via DRAFTENGINE_UPDATE_KEY' } else { 'not set (required for the real run)' }))
    Write-Host ''
    Write-Host 'Planned steps for the real run:'
    Write-Host '  1. Verify the installer exists and hash it (SHA-256).'
    Write-Host '  2. Write an unsigned manifest into .release\ and sign it with tools\UpdateKeyTool.'
    Write-Host '  3. Verify the signed manifest and stage dist\<version>\latest.json.'
    Write-Host '  4. Print the gh release command (upload only with -Publish).'
    Write-Host ("Dry-run marker written: {0}" -f $dryRunMarker)
    return
}

# --- Non-dry run safety gates ----------------------------------------------
if (-not (Test-Path -LiteralPath $dryRunMarker)) {
    Fail "Run this script with -DryRun first. Expected marker: $dryRunMarker"
}
if (-not $env:DRAFTENGINE_UPDATE_KEY -or -not (Test-Path -LiteralPath $env:DRAFTENGINE_UPDATE_KEY)) {
    Fail 'Set DRAFTENGINE_UPDATE_KEY to the private key path (outside the repository).'
}
if (-not $installerExists) {
    Fail "Installer not found: $installer`nRun scripts\build-installer.ps1 first."
}

$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $installer).Length

$distDir = Join-Path $repoRoot ("dist\{0}" -f $Version)
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$unsignedManifest = Join-Path $dryRunDir ("manifest-{0}.unsigned.json" -f $Version)
$signedManifest = Join-Path $distDir 'latest.json'

$releaseNotes = @{}
foreach ($property in $notes.PSObject.Properties) {
    $releaseNotes[$property.Name] = [string]$property.Value
}

[pscustomobject]@{
    schemaVersion = 1
    version = $Version
    publishedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    releaseNotes = $releaseNotes
    installer = [pscustomobject]@{
        url = $downloadUrl
        fileName = $fileName
        sizeBytes = $size
        sha256 = $hash
    }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $unsignedManifest -Encoding UTF8

$tool = Join-Path $repoRoot 'tools\UpdateKeyTool\bin\Release\net8.0\UpdateKeyTool.exe'
if (-not (Test-Path -LiteralPath $tool)) {
    Write-Host 'Building UpdateKeyTool...'
    & dotnet build (Join-Path $repoRoot 'tools\UpdateKeyTool\UpdateKeyTool.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail "UpdateKeyTool build failed ($LASTEXITCODE)." }
}

Write-Host 'Signing the manifest...'
& $tool sign --manifest $unsignedManifest --key $env:DRAFTENGINE_UPDATE_KEY --out $signedManifest
if ($LASTEXITCODE -ne 0) { Fail "Signing failed ($LASTEXITCODE)." }

$publicKey = (Get-Content -LiteralPath (Join-Path $repoRoot 'DraftEngine.App\appsettings.json') -Raw |
    Select-String -Pattern '"PublicKey"\s*:\s*"([^"]*)"').Matches[0].Groups[1].Value
if ([string]::IsNullOrWhiteSpace($publicKey)) {
    Fail 'Updates:PublicKey in appsettings.json is empty, so clients would reject this manifest.'
}

Write-Host 'Verifying the signed manifest...'
& $tool verify --manifest $signedManifest --public-key $publicKey
if ($LASTEXITCODE -ne 0) { Fail "Manifest verification failed ($LASTEXITCODE)." }

Copy-Item -LiteralPath $installer -Destination (Join-Path $distDir $fileName) -Force
Write-Host ("Staged: {0}" -f $distDir)

$assets = @((Join-Path $distDir $fileName), $signedManifest)
$publishArgs = @('release', 'create', $tag) + $assets + @('--title', "DraftEngine $Version", '--notes-file', [string]$notesPath)
if ($Prerelease) { $publishArgs += '--prerelease' }
$publishArgs += '--draft'

if ($Publish) {
    Write-Host 'Creating a DRAFT GitHub release (nothing goes live until you publish it)...'
    & gh @publishArgs
    if ($LASTEXITCODE -ne 0) { Fail "gh release create failed ($LASTEXITCODE)." }
}
else {
    Write-Host 'Nothing was uploaded. To create a draft release yourself, run:'
    Write-Host ('  gh ' + ($publishArgs -join ' '))
}
