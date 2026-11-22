# Register-SE_HardwareInserter.ps1
#
# Registra el add-in SE_HardwareInserter.AddIn en Solid Edge (HKCU, por usuario).
# Requiere PowerShell 64-bit, SDK .NET 10 y .NET Framework 4.8.
#
# Uso:
#   .\dev\register-addin.ps1              # compila + registra (Debug x64)
#   .\dev\register-addin.ps1 -Config Release
#
# No requiere administrador (registro HKCU).

param(
    [string]$Config = "Debug"
)

$ErrorActionPreference = "Stop"

# Resolver rutas relativas al directorio del script.
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "SE_HardwareInserter.slnx"
$addinCsproj = Join-Path $repoRoot "src\SE_HardwareInserter.AddIn\SE_HardwareInserter.AddIn.csproj"
$addinDll = Join-Path $repoRoot "src\SE_HardwareInserter.AddIn\bin\x64\$Config\net48\SE_HardwareInserter.AddIn.dll"
$registrationModule = Join-Path $PSScriptRoot "SEHardwareInserter.Registration.psm1"

# Restaurar el grafo versionado antes de compilar sin restauración implícita.
Write-Host "Restaurando dependencias en modo bloqueado..." -ForegroundColor Cyan
dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "La restauración bloqueada falló."
}

# Compilar el add-in (x64).
Write-Host "Compilando SE_HardwareInserter.AddIn ($Config, x64)..." -ForegroundColor Cyan
dotnet build $addinCsproj -c $Config -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "La compilación falló."
}

if (-not (Test-Path $addinDll)) {
    throw "No se encontró '$addinDll' tras compilar. ¿Es la configuración correcta?"
}

# El módulo comparte el mismo contrato declarativo usado por el instalador.
Import-Module $registrationModule -Force
Write-Host "Registrando y validando el add-in COM en HKCU (vista de 64 bits)..." -ForegroundColor Cyan
Register-SEHardwareInserterAddIn -AssemblyPath $addinDll

Write-Host ""
Write-Host "Add-in registrado correctamente." -ForegroundColor Green
Write-Host "DLL: $addinDll"
Write-Host "CLSID: {DC87125A-6DBF-43A5-968B-2578CCBFC158}"
Write-Host "ProgID: SE_HardwareInserter.AddIn"
Write-Host "Alias: HardwareInserter.AddIn"
Write-Host ""
Write-Host "Abra Solid Edge con un ensamblaje activo; debería aparecer el botón 'Insertar Hardware' en la ribbon." -ForegroundColor Yellow
