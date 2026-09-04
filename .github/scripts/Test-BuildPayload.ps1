[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BuildOutput,

    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($BuildOutput)
if (-not (Test-Path -LiteralPath $output -PathType Container)) {
    throw "No existe el directorio de build '$output'."
}

$ownDlls = @(
    'SE_HardwareInserter.AddIn.dll',
    'SE_HardwareInserter.Core.dll',
    'SE_HardwareInserter.SolidEdge.dll'
)
$expectedDlls = @($ownDlls + 'Interop.SolidEdge.dll')
foreach ($name in $expectedDlls) {
    $path = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta el payload obligatorio '$path'."
    }
    if ((Get-Item -LiteralPath $path).Length -le 0) {
        throw "El payload '$path' está vacío."
    }
}

$unexpected = @(Get-ChildItem -LiteralPath $output -File -Filter '*.dll' |
    Where-Object { $_.Name -notin $expectedDlls })
if ($unexpected.Count -gt 0) {
    throw "DLL no declaradas en el payload: $($unexpected.Name -join ', ')."
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $expectedFileVersion = "$Version.0"
    foreach ($name in $ownDlls) {
        $path = Join-Path $output $name
        $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
        if ($actual -ne $expectedFileVersion) {
            throw "FileVersion inesperada en '$name': '$actual'; esperada '$expectedFileVersion'."
        }
    }
}

$addinPath = Join-Path $output 'SE_HardwareInserter.AddIn.dll'
$stream = $null
$reader = $null
try {
    $stream = [IO.File]::Open($addinPath, 'Open', 'Read', 'ReadWrite')
    $reader = New-Object IO.BinaryReader($stream)
    if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Cabecera MZ inválida en el add-in.' }
    $stream.Position = 0x3C
    $peOffset = $reader.ReadInt32()
    $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550) { throw 'Cabecera PE inválida en el add-in.' }
    if ($reader.ReadUInt16() -ne 0x8664) { throw 'El add-in no está compilado para x64.' }
}
finally {
    if ($null -ne $reader) { $reader.Dispose() }
    elseif ($null -ne $stream) { $stream.Dispose() }
}

Write-Host "Payload explícito válido en '$output'." -ForegroundColor Green
