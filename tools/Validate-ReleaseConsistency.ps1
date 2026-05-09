param(
    [string]$ProjectFile = "UndercutterFFXIV.csproj",
    [string]$PrimaryFeed = "pluginmaster.json",
    [string]$SecondaryFeed = "pluginmaster-live.json",
    [string]$BuiltManifest = "bin/Release/UndercutterFFXIV.json",
    [string]$ReleaseAsset = "bin/Release/UndercutterFFXIV-v1.1.36.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Read-FeedEntry([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        Fail "Feed file is empty: $Path"
    }

    $json = $raw | ConvertFrom-Json
    if ($json -is [System.Array]) {
        if ($json.Count -lt 1) { Fail "Feed array is empty: $Path" }
        return $json[0]
    }

    return $json
}

function Get-ZipManifestAssemblyVersion([string]$ZipPath) {
    if (-not (Test-Path -LiteralPath $ZipPath)) {
        return $null
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $ZipPath))
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "UndercutterFFXIV.json" } | Select-Object -First 1
        if ($null -eq $entry) {
            Fail "Release asset missing UndercutterFFXIV.json: $ZipPath"
        }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try {
            $manifestRaw = $reader.ReadToEnd()
        }
        finally {
            $reader.Close()
        }

        $manifest = $manifestRaw | ConvertFrom-Json
        return [string]$manifest.AssemblyVersion
    }
    finally {
        $zip.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    Fail "Project file not found: $ProjectFile"
}

[xml]$proj = Get-Content -LiteralPath $ProjectFile -Raw
$projectVersion = [string]$proj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    Fail "Could not read <Version> from $ProjectFile"
}

$expectedAssembly = "$projectVersion.0"
$primary = Read-FeedEntry $PrimaryFeed
if ($null -eq $primary) {
    Fail "Primary feed file missing: $PrimaryFeed"
}

if ([string]$primary.AssemblyVersion -ne $expectedAssembly) {
    Fail "Primary feed AssemblyVersion mismatch: expected $expectedAssembly got $($primary.AssemblyVersion)"
}

if ([string]$primary.TestingAssemblyVersion -ne $expectedAssembly) {
    Fail "Primary feed TestingAssemblyVersion mismatch: expected $expectedAssembly got $($primary.TestingAssemblyVersion)"
}

$expectedTagSegment = "/download/v$projectVersion/"
foreach ($name in @("DownloadLinkInstall", "DownloadLinkUpdate", "DownloadLinkTesting")) {
    $url = [string]$primary.$name
    if ([string]::IsNullOrWhiteSpace($url)) {
        Fail "Primary feed missing $name"
    }
    if ($url -notlike "*$expectedTagSegment*") {
        Fail "Primary feed $name does not contain tag segment $expectedTagSegment"
    }
}

if (([string]$primary.DownloadLinkInstall) -ne ([string]$primary.DownloadLinkUpdate) -or
    ([string]$primary.DownloadLinkInstall) -ne ([string]$primary.DownloadLinkTesting)) {
    Fail "Primary feed download links do not match"
}

$secondary = Read-FeedEntry $SecondaryFeed
if ($null -ne $secondary) {
    if ([string]$secondary.AssemblyVersion -ne $expectedAssembly) {
        Fail "Secondary feed AssemblyVersion mismatch: expected $expectedAssembly got $($secondary.AssemblyVersion)"
    }
    foreach ($name in @("DownloadLinkInstall", "DownloadLinkUpdate", "DownloadLinkTesting")) {
        $url = [string]$secondary.$name
        if ($url -notlike "*$expectedTagSegment*") {
            Fail "Secondary feed $name does not contain tag segment $expectedTagSegment"
        }
    }
}

if (Test-Path -LiteralPath $BuiltManifest) {
    $built = Get-Content -LiteralPath $BuiltManifest -Raw | ConvertFrom-Json
    if ([string]$built.AssemblyVersion -ne $expectedAssembly) {
        Fail "Built manifest AssemblyVersion mismatch: expected $expectedAssembly got $($built.AssemblyVersion)"
    }
}

$zipAssembly = Get-ZipManifestAssemblyVersion $ReleaseAsset
if ($null -ne $zipAssembly -and $zipAssembly -ne $expectedAssembly) {
    Fail "Release asset manifest AssemblyVersion mismatch: expected $expectedAssembly got $zipAssembly"
}

Write-Host "Release consistency OK"
Write-Host "Project version: $projectVersion"
Write-Host "Assembly version: $expectedAssembly"
Write-Host "Primary feed URL: $($primary.DownloadLinkInstall)"
if ($null -ne $secondary) {
    Write-Host "Secondary feed URL: $($secondary.DownloadLinkInstall)"
}
if ($null -ne $zipAssembly) {
    Write-Host "Asset manifest version: $zipAssembly"
}
