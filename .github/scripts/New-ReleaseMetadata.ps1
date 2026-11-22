[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SbomTool,
    [Parameter(Mandatory = $true)][string]$ReleaseDirectory,
    [Parameter(Mandatory = $true)][string]$SbomInputDirectory,
    [Parameter(Mandatory = $true)][string]$PayloadDirectory,
    [Parameter(Mandatory = $true)][string]$BuildMetadataDirectory,
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repository
)

$ErrorActionPreference = 'Stop'
$release = [IO.Path]::GetFullPath($ReleaseDirectory)
$input = [IO.Path]::GetFullPath($SbomInputDirectory)
$payload = [IO.Path]::GetFullPath($PayloadDirectory)
$metadata = [IO.Path]::GetFullPath($BuildMetadataDirectory)
$installer = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "No existe '$installer'." }
if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw "No existe '$payload'." }
if (-not (Test-Path -LiteralPath $metadata -PathType Container)) { throw "No existe '$metadata'." }
if (-not (Test-Path -LiteralPath $SbomTool -PathType Leaf)) { throw "No existe '$SbomTool'." }
if (Test-Path -LiteralPath $input) { throw "La entrada SBOM ya existe: '$input'." }

$payloadInput = Join-Path $input 'payload'
$metadataInput = Join-Path $input 'build-metadata'
[IO.Directory]::CreateDirectory($payloadInput) | Out-Null
[IO.Directory]::CreateDirectory($metadataInput) | Out-Null
$expectedPayload = @(
    'SE_HardwareInserter.AddIn.dll',
    'SE_HardwareInserter.Core.dll',
    'SE_HardwareInserter.SolidEdge.dll',
    'Interop.SolidEdge.dll'
)
foreach ($name in $expectedPayload) {
    $source = Join-Path $payload $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Falta '$source'." }
    [IO.File]::Copy($source, (Join-Path $payloadInput $name), $false)
}
[IO.File]::Copy($installer, (Join-Path $input ([IO.Path]::GetFileName($installer))), $false)

foreach ($file in Get-ChildItem -LiteralPath $metadata -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($metadata, $file.FullName)
    $destination = Join-Path $metadataInput $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::Copy($file.FullName, $destination, $false)
}
if (@(Get-ChildItem -LiteralPath $metadataInput -Recurse -File -Filter 'project.assets.json').Count -eq 0) {
    throw 'La entrada SBOM no contiene project.assets.json del build restaurado.'
}

& $SbomTool generate -b $input -bc $metadataInput -m $release `
    -pn 'SE_HardwareInserter' -pv $Version -ps 'Gabriel Spoleto' `
    -nsb "https://github.com/$Repository"
if ($LASTEXITCODE -ne 0) { throw "sbom-tool generate devolvió $LASTEXITCODE." }

$generated = Join-Path $release '_manifest\spdx_2.2\manifest.spdx.json'
if (-not (Test-Path -LiteralPath $generated -PathType Leaf)) { throw "No se generó '$generated'." }
$document = Get-Content -LiteralPath $generated -Raw | ConvertFrom-Json
$documentFiles = @($document.files | ForEach-Object {
    [IO.Path]::GetFileName(([string]$_.fileName).Replace('/', '\'))
})
foreach ($name in @($expectedPayload + [IO.Path]::GetFileName($installer))) {
    if ($name -notin $documentFiles) { throw "El SBOM no contiene el payload '$name'." }
}

foreach ($expectedPackage in @(
    @{ Name = 'Interop.SolidEdge'; Version = '220.2.0' },
    @{ Name = 'Microsoft.NETFramework.ReferenceAssemblies.net48'; Version = '1.0.3' }
)) {
    $match = @($document.packages | Where-Object {
        ([string]$_.name).Equals($expectedPackage.Name, [StringComparison]::OrdinalIgnoreCase) -and
        ([string]$_.versionInfo).Equals($expectedPackage.Version, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($match.Count -eq 0) {
        throw "El SBOM no contiene $($expectedPackage.Name) $($expectedPackage.Version)."
    }
}

$validation = Join-Path ([IO.Path]::GetTempPath()) ("SE_HardwareInserter-sbom-validation-$Version.json")
try {
    & $SbomTool validate -b $input -m (Join-Path $release '_manifest') -o $validation -mi 'SPDX:2.2'
    if ($LASTEXITCODE -ne 0) { throw "sbom-tool validate devolvió $LASTEXITCODE." }
}
finally {
    if (Test-Path -LiteralPath $validation) { [IO.File]::Delete($validation) }
}

$sbom = Join-Path $release "SE_HardwareInserter-$Version.spdx.json"
[IO.File]::Copy($generated, $sbom, $false)
$checksumPath = Join-Path $release "SE_HardwareInserter-$Version.sha256"
$lines = foreach ($path in @($installer, $sbom)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$([IO.Path]::GetFileName($path))"
}
[IO.File]::WriteAllLines($checksumPath, $lines, [Text.UTF8Encoding]::new($false))
[IO.Directory]::Delete($input, $true)
Write-Host "SBOM SPDX 2.2 validado con payload y metadata de build." -ForegroundColor Green
