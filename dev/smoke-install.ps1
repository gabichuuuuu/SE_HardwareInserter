[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"

if (-not [Environment]::Is64BitProcess) {
    throw "El smoke debe ejecutarse desde PowerShell de 64 bits."
}

$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$startArguments = @{
    FilePath = $resolvedInstaller
    ArgumentList = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-")
    Wait = $true
    PassThru = $true
    WindowStyle = "Hidden"
}
$process = Start-Process @startArguments
if ($process.ExitCode -ne 0) {
    throw "El instalador devolvió $($process.ExitCode)."
}

$installDirectory = Join-Path $env:LOCALAPPDATA "SE_HardwareInserter"
$expectedFiles = @(
    "SE_HardwareInserter.AddIn.dll",
    "SE_HardwareInserter.Core.dll",
    "SE_HardwareInserter.SolidEdge.dll",
    "Interop.SolidEdge.dll"
)
foreach ($fileName in $expectedFiles) {
    $path = Join-Path $installDirectory $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta el payload instalado '$path'."
    }
}

$unexpectedPayload = @(
    Get-ChildItem -LiteralPath $installDirectory -File |
        Where-Object { $_.Extension -in @(".pdb", ".xml") }
)
if ($unexpectedPayload.Count -ne 0) {
    throw "El instalador incluyó payload de desarrollo: $($unexpectedPayload.Name -join ', ')."
}

Import-Module (Join-Path $PSScriptRoot "SEHardwareInserter.Registration.psm1") -Force
Test-SEHardwareInserterRegistration -AssemblyPath (
    Join-Path $installDirectory "SE_HardwareInserter.AddIn.dll") -Activate |
    Out-Null

Write-Host "Smoke de instalación y activación COM correcto." -ForegroundColor Green
