# Unregister-SE_HardwareInserter.ps1
#
# Desregistra el add-in SE_HardwareInserter.AddIn de Solid Edge.
# Borra el CLSID de HKCU y la subclave HKCU\Software\Solid Edge\Add-Ins\{CLSID}.
#
# Uso:
#   .\dev\unregister-addin.ps1

param()

$ErrorActionPreference = "Stop"

$registrationModule = Join-Path $PSScriptRoot "SEHardwareInserter.Registration.psm1"
Import-Module $registrationModule -Force

Write-Host "Eliminando el registro COM per-user exacto..." -ForegroundColor Cyan
Unregister-SEHardwareInserterAddIn

$contract = Get-SEHardwareInserterRegistrationContract
$remainingKeys = @(
    "Registry::HKEY_CURRENT_USER\Software\Classes\$($contract.CanonicalProgId)",
    "Registry::HKEY_CURRENT_USER\Software\Classes\$($contract.LegacyProgId)",
    "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\$($contract.Clsid)",
    "Registry::HKEY_CURRENT_USER\Software\Solid Edge\Add-Ins\$($contract.Clsid)"
) | Where-Object { Test-Path -LiteralPath $_ }
if ($remainingKeys.Count -ne 0) {
    throw "El desregistro dejó claves residuales: $($remainingKeys -join ', ')"
}

Write-Host ""
Write-Host "Add-in desregistrado." -ForegroundColor Green
