[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BuildOutput,

    [Parameter(Mandatory = $true)]
    [string]$StageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$stage = [IO.Path]::GetFullPath($StageDirectory)
if (Test-Path -LiteralPath $stage) {
    throw "El staging '$stage' ya existe; se rechaza reutilizar contenido de otra ejecución."
}

& (Join-Path $PSScriptRoot 'Test-BuildPayload.ps1') -BuildOutput $BuildOutput -Version $Version
[IO.Directory]::CreateDirectory($stage) | Out-Null
foreach ($name in @(
    'SE_HardwareInserter.AddIn.dll',
    'SE_HardwareInserter.Core.dll',
    'SE_HardwareInserter.SolidEdge.dll',
    'Interop.SolidEdge.dll'
)) {
    [IO.File]::Copy(
        (Join-Path ([IO.Path]::GetFullPath($BuildOutput)) $name),
        (Join-Path $stage $name),
        $false)
}

& (Join-Path $PSScriptRoot 'Test-BuildPayload.ps1') -BuildOutput $stage -Version $Version
Write-Host "Staging explícito creado en '$stage'." -ForegroundColor Green
