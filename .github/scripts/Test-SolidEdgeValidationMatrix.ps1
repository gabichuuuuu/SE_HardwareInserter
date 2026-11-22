[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit,
    [string]$ValidationDirectory = (
        Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'validation\solid-edge')
)

$ErrorActionPreference = 'Stop'
$expectedCommitNormalized = $ExpectedCommit.ToLowerInvariant()
if ($expectedCommitNormalized -notmatch '^[0-9a-f]{40}$') { throw 'ExpectedCommit no es un SHA-1 válido.' }
$requiredChecks = @(
    'cleanInstall','updateFromV010','ribbonVisible','initialCatalogLoaded','cachePathExact',
    'freeInsertionWithoutHole','automaticConstraints','guidedSelection','unsupportedRollback',
    'documentSwitchInvalidatesCapture','immutableCache','reopenAssembly','uninstallCleanup'
)
function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return (($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)) |
            ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally { $sha.Dispose() }
}

function Test-UtcRoundTripTimestamp {
    param([Parameter(Mandatory = $true)][object]$Value)

    if ($Value -is [DateTime]) {
        return $Value.Kind -eq [DateTimeKind]::Utc
    }
    if ($Value -is [DateTimeOffset]) {
        return $Value.Offset -eq [TimeSpan]::Zero
    }
    if ($Value -isnot [string]) {
        return $false
    }

    $parsed = [DateTimeOffset]::MinValue
    return $Value.EndsWith('Z', [StringComparison]::Ordinal) -and
        [DateTimeOffset]::TryParseExact(
            $Value,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$parsed) -and
        $parsed.Offset -eq [TimeSpan]::Zero
}

$root = [IO.Path]::GetFullPath($ValidationDirectory)
$commonInstallerSha256 = $null
foreach ($version in 2022..2026) {
    $path = Join-Path $root "SE$version.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta '$path'." }
    $evidence = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($evidence.schemaVersion -ne 1 -or $evidence.result -ne 'pass') { throw "SE$($version): resultado/schema inválido." }
    if ([int]$evidence.solidEdgeVersion -ne $version) { throw "SE$($version): versión incoherente." }
    if ([string]$evidence.solidEdgeProductVersion -notmatch '^\d+(?:\.\d+)+$') {
        throw "SE$($version): solidEdgeProductVersion debe ser numérico con puntos."
    }
    if ([string]$evidence.commitSha -ne $expectedCommitNormalized -or
        [string]$evidence.sourceCommitSha -ne $expectedCommitNormalized) {
        throw "SE$($version): source commit incorrecto."
    }
    if ([string]$evidence.installerSha256 -notmatch '^[0-9a-f]{64}$' -or
        [string]$evidence.libraryManifest.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "SE$($version): hash inválido."
    }
    if ($null -eq $commonInstallerSha256) {
        $commonInstallerSha256 = [string]$evidence.installerSha256
    }
    elseif ([string]$evidence.installerSha256 -ne $commonInstallerSha256) {
        throw "SE$($version): installerSha256 difiere del resto de la matriz."
    }
    $files = @($evidence.libraryManifest.files)
    if ($files.Count -eq 0 -or [int]$evidence.libraryManifest.fileCount -ne $files.Count) {
        throw "SE$($version): manifest vacío o incoherente."
    }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $manifestLines = foreach ($file in ($files | Sort-Object relativePath)) {
        $relativePath = ([string]$file.relativePath).Replace('\', '/')
        $sha256 = [string]$file.sha256
        $segments = @($relativePath.Split('/') | Where-Object { $_.Length -gt 0 })
        if ([string]::IsNullOrWhiteSpace($relativePath) -or $relativePath.StartsWith('/') -or
            $relativePath.StartsWith('//') -or $relativePath -match '^[a-zA-Z]:' -or
            [IO.Path]::IsPathRooted($relativePath) -or $segments -contains '..' -or
            $sha256 -notmatch '^[0-9a-f]{64}$' -or
            -not $seen.Add($relativePath)) {
            throw "SE$($version): entrada de manifest inválida '$relativePath'."
        }
        "$relativePath`t$sha256"
    }
    $canonical = [string]::Join([char]10, @($manifestLines)) + [char]10
    if ((Get-TextSha256 $canonical) -ne [string]$evidence.libraryManifest.sha256) {
        throw "SE$($version): hash de manifest no coincide."
    }
    foreach ($check in $requiredChecks) {
        $property = $evidence.checks.PSObject.Properties[$check]
        if ($null -eq $property -or $property.Value -ne $true) { throw "SE$($version): check '$check' no aprobado." }
    }
    if (-not (Test-UtcRoundTripTimestamp $evidence.timestampUtc)) {
        throw "SE$($version): timestampUtc no es ISO 8601 UTC semánticamente válido."
    }
}
Write-Host "Matriz Solid Edge 2022-2026 válida para '$expectedCommitNormalized'." -ForegroundColor Green
