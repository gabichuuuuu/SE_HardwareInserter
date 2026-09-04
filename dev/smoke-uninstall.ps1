[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

if (-not [Environment]::Is64BitProcess) {
    throw "El smoke debe ejecutarse desde PowerShell de 64 bits."
}

$installDirectory = Join-Path $env:LOCALAPPDATA "SE_HardwareInserter"
$uninstaller = Join-Path $installDirectory "unins000.exe"
if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw "No existe el desinstalador '$uninstaller'."
}

$startArguments = @{
    FilePath = $uninstaller
    ArgumentList = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
    Wait = $true
    PassThru = $true
    WindowStyle = "Hidden"
}
$process = Start-Process @startArguments
if ($process.ExitCode -ne 0) {
    throw "El desinstalador devolvió $($process.ExitCode)."
}

$deadline = [DateTime]::UtcNow.AddSeconds(5)
while ((Test-Path -LiteralPath $uninstaller -PathType Leaf) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 100
}
if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
    throw "El desinstalador no eliminó '$uninstaller'."
}

$knownFiles = @(
    "SE_HardwareInserter.AddIn.dll",
    "SE_HardwareInserter.Core.dll",
    "SE_HardwareInserter.SolidEdge.dll",
    "Interop.SolidEdge.dll"
)
$remainingFiles = @(
    $knownFiles |
        ForEach-Object { Join-Path $installDirectory $_ } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
)
if ($remainingFiles.Count -ne 0) {
    throw "La desinstalación dejó binarios conocidos: $($remainingFiles -join ', ')."
}

Import-Module (Join-Path $PSScriptRoot "SEHardwareInserter.Registration.psm1") -Force
$contract = Get-SEHardwareInserterRegistrationContract
$remainingKeys = @(
    "Registry::HKEY_CURRENT_USER\Software\Classes\$($contract.CanonicalProgId)",
    "Registry::HKEY_CURRENT_USER\Software\Classes\$($contract.LegacyProgId)",
    "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\$($contract.Clsid)",
    "Registry::HKEY_CURRENT_USER\Software\Solid Edge\Add-Ins\$($contract.Clsid)",
    "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($contract.AppId)_is1"
) | Where-Object { Test-Path -LiteralPath $_ }
if ($remainingKeys.Count -ne 0) {
    throw "La desinstalación dejó claves conocidas: $($remainingKeys -join ', ')."
}


if ([IO.Directory]::Exists($installDirectory)) {
    $residualEntries = @([IO.Directory]::GetFileSystemEntries($installDirectory))
    if ($residualEntries.Count -ne 0) {
        throw "La carpeta de instalación conserva entradas que no se borraron por ser ambiguas: $($residualEntries -join ', ')"
    }
}

Write-Host "Smoke de desinstalación correcto; no se eliminaron datos de usuario ambiguos." -ForegroundColor Green
