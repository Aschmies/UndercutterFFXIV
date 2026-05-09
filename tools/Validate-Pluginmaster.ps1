param(
    [Parameter(Mandatory = $false)]
    [string]$Path = "pluginmaster.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

if (-not (Test-Path -LiteralPath $Path)) {
    Fail "File not found: $Path"
}

$raw = Get-Content -LiteralPath $Path -Raw
if ([string]::IsNullOrWhiteSpace($raw)) {
    Fail "pluginmaster is empty"
}

if ($raw -match "<<<<<<<|=======|>>>>>>>") {
    Fail "pluginmaster appears to contain unresolved merge conflict markers"
}

try {
    $json = $raw | ConvertFrom-Json
}
catch {
    Fail "Invalid JSON: $($_.Exception.Message)"
}

if ($null -eq $json) {
    Fail "Parsed JSON is null"
}

$entries = @()
if ($json -is [System.Array]) {
    $entries = $json
}
else {
    Fail "Root must be a JSON array"
}

if ($entries.Count -lt 1) {
    Fail "Root array must contain at least one plugin entry"
}

$required = @(
    "Author",
    "Name",
    "InternalName",
    "AssemblyVersion",
    "Description",
    "ApplicableVersion",
    "RepoUrl",
    "DalamudApiLevel",
    "DownloadCount",
    "DownloadLinkInstall",
    "DownloadLinkTesting",
    "DownloadLinkUpdate",
    "Changelog",
    "Tags",
    "IsHide",
    "IsTestingExclusive",
    "IconUrl",
    "Punchline",
    "AcceptsFeedback",
    "LastUpdated"
)

$semver3Or4 = '^\d+\.\d+\.\d+(\.\d+)?$'
$lastEntry = $entries[0]

foreach ($key in $required) {
    if ($null -eq $lastEntry.PSObject.Properties[$key]) {
        Fail "Missing required key on first entry: $key"
    }
}

$version = [string]$lastEntry.AssemblyVersion
if ($version -notmatch $semver3Or4) {
    Fail "AssemblyVersion '$version' must match major.minor.patch(.revision)"
}

$normalizedVersion = if ($version -match '^\d+\.\d+\.\d+\.\d+$') { $version.Substring(0, $version.LastIndexOf('.')) } else { $version }

$installUrl = [string]$lastEntry.DownloadLinkInstall
$updateUrl = [string]$lastEntry.DownloadLinkUpdate
$testingUrl = [string]$lastEntry.DownloadLinkTesting

if ($installUrl -ne $updateUrl -or $installUrl -ne $testingUrl) {
    Fail "Install/Update/Testing download links must match"
}

$expectedTagSegment = "/download/v$normalizedVersion/"
if ($installUrl -notlike "*$expectedTagSegment*") {
    Fail "Download link does not match AssemblyVersion tag segment $expectedTagSegment"
}

$requiredArrayKeys = @("Tags")
foreach ($arrKey in $requiredArrayKeys) {
    $value = $lastEntry.$arrKey
    if (-not ($value -is [System.Array])) {
        Fail "$arrKey must be an array"
    }
}

if ($lastEntry.LastUpdated -is [ValueType]) {
    $lastUpdatedNumber = [double]$lastEntry.LastUpdated
    if ($lastUpdatedNumber -le 0) {
        Fail "LastUpdated numeric value must be > 0"
    }
}
else {
    try {
        $null = [DateTimeOffset]::Parse([string]$lastEntry.LastUpdated)
    }
    catch {
        Fail "LastUpdated is not a valid date-time or unix timestamp: $($lastEntry.LastUpdated)"
    }
}

Write-Host "pluginmaster validation OK"
Write-Host "Entry count: $($entries.Count)"
Write-Host "Current release version: $version"
exit 0
