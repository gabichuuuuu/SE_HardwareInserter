[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(2022, 2023, 2024, 2025, 2026)]
    [int]$SolidEdgeVersion,
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,
    [Parameter(Mandatory = $true)]
    [string]$TestLibraryPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$ExpectedSourceCommit = '',
    [string]$SourceCommitSidecarPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-Confirmation {
    param([Parameter(Mandatory = $true)][string]$Prompt)
    while ($true) {
        $answer = (Read-Host "$Prompt [s/n]").Trim()
        if ($answer -match '^(?i:s|si|sí|y|yes)$') { return $true }
        if ($answer -match '^(?i:n|no)$') { return $false }
        Write-Host 'Respuesta no válida. Use s o n.' -ForegroundColor Yellow
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)
    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
        return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally { $sha.Dispose() }
}

function Get-SolidEdgeExecutable {
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    foreach ($keyPath in @(
        'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Edge.exe',
        'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Edge.exe'
    )) {
        if (Test-Path -LiteralPath $keyPath) {
            $value = (Get-Item -LiteralPath $keyPath).GetValue('')
            if (-not [string]::IsNullOrWhiteSpace([string]$value)) { $candidates.Add([string]$value) }
        }
    }
    foreach ($programFilesRoot in @(
        $env:ProgramFiles,
        [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    )) {
        if ([string]::IsNullOrWhiteSpace($programFilesRoot)) { continue }
        $siemensRoot = Join-Path $programFilesRoot 'Siemens'
        foreach ($directory in @(Get-ChildItem -LiteralPath $siemensRoot -Directory -Filter 'Solid Edge*' -ErrorAction SilentlyContinue)) {
            $candidates.Add((Join-Path $directory.FullName 'Program\Edge.exe'))
        }
    }
    return $candidates | Select-Object -Unique |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
    throw 'La aceptación debe ejecutarse desde PowerShell de 64 bits sobre Windows x64.'
}
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$libraryRoot = (Resolve-Path -LiteralPath $TestLibraryPath).Path
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "No existe '$installer'." }
if (-not (Test-Path -LiteralPath $libraryRoot -PathType Container)) { throw "No existe '$libraryRoot'." }
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetFileName($output) -ne "SE$SolidEdgeVersion.json") {
    throw "El resultado debe llamarse 'SE$SolidEdgeVersion.json'."
}
if (Test-Path -LiteralPath $output) { throw "La evidencia '$output' ya existe." }

$commitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'No se pudo determinar el source commit.'
}
$porcelain = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or $porcelain.Count -ne 0) {
    throw "El repositorio debe estar completamente limpio, incluidos archivos untracked: $($porcelain -join ', ')."
}

$sidecarCommit = ''
if (-not [string]::IsNullOrWhiteSpace($SourceCommitSidecarPath)) {
    $sidecar = (Resolve-Path -LiteralPath $SourceCommitSidecarPath).Path
    $sidecarCommit = (Get-Content -LiteralPath $sidecar -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($ExpectedSourceCommit) -and [string]::IsNullOrWhiteSpace($sidecarCommit)) {
    throw 'Indique ExpectedSourceCommit o SourceCommitSidecarPath del candidato CI.'
}
$expectedCommit = if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceCommit)) {
    $ExpectedSourceCommit.Trim().ToLowerInvariant()
}
else {
    $sidecarCommit.ToLowerInvariant()
}
if ($expectedCommit -notmatch '^[0-9a-f]{40}$' -or
    (-not [string]::IsNullOrWhiteSpace($sidecarCommit) -and $sidecarCommit.ToLowerInvariant() -ne $expectedCommit) -or
    $commitSha.ToLowerInvariant() -ne $expectedCommit) {
    throw 'El sidecar/parámetro, HEAD y sourceCommitSha deben coincidir.'
}

if ($null -eq [Type]::GetTypeFromProgID('SolidEdge.Application', $false)) {
    throw 'SolidEdge.Application no está registrado en COM.'
}
$edgeExecutable = Get-SolidEdgeExecutable
if ([string]::IsNullOrWhiteSpace($edgeExecutable)) { throw 'No se encontró Edge.exe.' }
$edgeProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($edgeExecutable).ProductVersion
if ($edgeProductVersion -notmatch '^\d+(?:\.\d+)+$') {
    throw "Solid Edge devolvió un ProductVersion no numérico: '$edgeProductVersion'."
}
Write-Host "Solid Edge detectado. ProductVersion: $edgeProductVersion" -ForegroundColor Cyan
if (-not (Get-Confirmation "Confirme que corresponde a Solid Edge $SolidEdgeVersion")) {
    throw "La instalación no fue confirmada como Solid Edge $SolidEdgeVersion."
}

$libraryFiles = @(
    Get-ChildItem -LiteralPath $libraryRoot -Recurse -File |
        Where-Object { $_.Extension -ieq '.par' } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($libraryRoot.TrimEnd('\').Length).TrimStart('\')
            [ordered]@{
                relativePath = $relativePath.Replace('\', '/')
                sha256 = Get-Sha256 $_.FullName
            }
        } | Sort-Object relativePath
)
if ($libraryFiles.Count -eq 0) { throw 'La librería debe contener al menos un .par.' }
$manifestLines = @($libraryFiles | ForEach-Object { "$($_.relativePath)`t$($_.sha256)" })
$manifestCanonical = [string]::Join([char]10, $manifestLines) + [char]10

$checkDefinitions = [ordered]@{
    cleanInstall = 'Instalación limpia per-user completada sin elevación'
    updateFromV010 = 'Actualización desde v0.1.0 completada'
    ribbonVisible = 'Botón Insertar Hardware visible en Assembly'
    initialCatalogLoaded = 'Catálogo visible en la primera apertura'
    cachePathExact = 'Caché creada bajo el directorio exacto del ensamblaje'
    freeInsertionWithoutHole = 'Inserción libre sin capturar agujero'
    automaticConstraints = 'Caso inequívoco restringido automáticamente'
    guidedSelection = 'Caso ambiguo completado mediante selección guiada'
    unsupportedRollback = 'Caso no soportado revertido sin occurrence residual'
    documentSwitchInvalidatesCapture = 'Cambio de documento invalida la captura'
    immutableCache = 'Nueva revisión no altera referencias antiguas'
    reopenAssembly = 'Reapertura conserva las referencias'
    uninstallCleanup = 'Desinstalación elimina binarios y claves conocidas'
}
$checks = [ordered]@{}
foreach ($entry in $checkDefinitions.GetEnumerator()) {
    $checks[$entry.Key] = Get-Confirmation $entry.Value
}
$notes = Read-Host 'Notas sin secretos, usuario, equipo ni rutas locales'
foreach ($forbidden in @($env:COMPUTERNAME, $env:USERNAME, $env:USERPROFILE)) {
    if (-not [string]::IsNullOrWhiteSpace($forbidden) -and
        $notes.IndexOf($forbidden, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw 'Las notas contienen información local prohibida.'
    }
}

$allPassed = @($checks.Values | Where-Object { $_ -ne $true }).Count -eq 0
$result = [ordered]@{
    schemaVersion = 1
    result = if ($allPassed) { 'pass' } else { 'fail' }
    solidEdgeVersion = $SolidEdgeVersion
    solidEdgeProductVersion = $edgeProductVersion
    commitSha = $commitSha.ToLowerInvariant()
    sourceCommitSha = $commitSha.ToLowerInvariant()
    installerFileName = [IO.Path]::GetFileName($installer)
    installerSha256 = Get-Sha256 $installer
    libraryManifest = [ordered]@{
        sha256 = Get-TextSha256 $manifestCanonical
        fileCount = $libraryFiles.Count
        files = $libraryFiles
    }
    timestampUtc = [DateTime]::UtcNow.ToString('O')
    checks = $checks
    notes = $notes
}
$outputDirectory = [IO.Path]::GetDirectoryName($output)
if (-not [string]::IsNullOrEmpty($outputDirectory)) { [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null }
$temporary = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $output)
}
finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
$readback = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
if ($readback.schemaVersion -ne 1 -or $readback.sourceCommitSha -ne $commitSha.ToLowerInvariant()) {
    throw 'El readback no coincide con la evidencia escrita.'
}
Write-Host "Resultado: $($readback.result). Archivo: $output" -ForegroundColor $(if ($allPassed) { 'Green' } else { 'Red' })
if (-not $allPassed) { throw 'Uno o más checks fallaron; la evidencia no desbloquea una release.' }
