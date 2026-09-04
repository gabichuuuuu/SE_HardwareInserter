[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $repoRoot "installer\SE_HardwareInserter.iss"
$installer = Get-Content -LiteralPath $installerPath -Raw
$goldenPath = Join-Path $PSScriptRoot "registration-contract.golden.json"
$golden = Get-Content -LiteralPath $goldenPath -Raw | ConvertFrom-Json

Import-Module (Join-Path $PSScriptRoot "SEHardwareInserter.Registration.psm1") -Force
$contract = Get-SEHardwareInserterRegistrationContract
$expectedAssembly =
    "$($contract.AssemblyName), Version=$($contract.AssemblyVersion), Culture=neutral, PublicKeyToken=null"

$goldenChecks = [ordered]@{
    Clsid = $contract.Clsid
    CanonicalProgId = $contract.CanonicalProgId
    LegacyProgId = $contract.LegacyProgId
    ClassName = $contract.ClassName
    AssemblyDisplayName = $expectedAssembly
    AssemblyVersion = $contract.AssemblyVersion
    ManagedCategory = $contract.ManagedCategory
    AppId = $contract.AppId
}
foreach ($entry in $goldenChecks.GetEnumerator()) {
    $property = $golden.PSObject.Properties[$entry.Key]
    if ($null -eq $property -or [string]$property.Value -ne [string]$entry.Value) {
        $actual = if ($null -eq $property) { '<missing>' } else { [string]$property.Value }
        throw "Golden '$($entry.Key)' no coincide con el módulo: '$actual' frente a '$($entry.Value)'."
    }
}

$clsidPath = "Software\Classes\CLSID\$($contract.Clsid)"
$expectedGoldenPaths = @(
    "Software\Classes\$($contract.CanonicalProgId)",
    "Software\Classes\$($contract.CanonicalProgId)\CLSID",
    "Software\Classes\$($contract.LegacyProgId)",
    "Software\Classes\$($contract.LegacyProgId)\CLSID",
    $clsidPath,
    "$clsidPath\InprocServer32",
    "$clsidPath\InprocServer32\$($contract.AssemblyVersion)",
    "$clsidPath\ProgId",
    "$clsidPath\Implemented Categories\$($contract.ManagedCategory)",
    "Software\Solid Edge\Add-Ins\$($contract.Clsid)"
)
if ((@($golden.registryPaths | Sort-Object) -join "`n") -ne
    (@($expectedGoldenPaths | Sort-Object) -join "`n")) {
    throw 'Las rutas del golden no coinciden exactamente con el contrato normalizado.'
}

$requiredGoldenValues = [ordered]@{
    'InprocServer32.default' = 'mscoree.dll'
    'InprocServer32.ThreadingModel' = 'Both'
    'InprocServer32.Class' = $contract.ClassName
    'InprocServer32.Assembly' = $expectedAssembly
    'InprocServer32.RuntimeVersion' = 'v4.0.30319'
    'InprocServer32.CodeBase' = '${CODEBASE_URI}'
    'SolidEdge.Description' = 'SE_HardwareInserter: inserción de tornillería y anchors desde librería local.'
    'SolidEdge.Summary' = 'Add-in para inserción automática de hardware en ensamblajes de Solid Edge.'
    'SolidEdge.Visible' = 1
}
foreach ($entry in $requiredGoldenValues.GetEnumerator()) {
    $property = $golden.requiredValues.PSObject.Properties[$entry.Key]
    if ($null -eq $property -or [string]$property.Value -ne [string]$entry.Value) {
        throw "El valor golden '$($entry.Key)' no coincide con el contrato."
    }
}

$requiredFragments = @(
    "#define AddInClass `"$($contract.ClassName)`"",
    "#define AddInAssembly `"$expectedAssembly`"",
    "#define AddInClsid `"{{$($contract.Clsid.Trim('{}'))}`"",
    "Root: HKCU64; Subkey: `"Software\Classes\SE_HardwareInserter.AddIn`"",
    "Software\Classes\SE_HardwareInserter.AddIn\CLSID",
    "Software\Classes\HardwareInserter.AddIn\CLSID",
    "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0",
    "Software\Classes\CLSID\{#AddInClsid}\ProgId",
    "Implemented Categories\{#ManagedCategory}",
    "Software\Solid Edge\Add-Ins\{#AddInClsid}",
    "Source: `"..\dev\installer-helper.ps1`"; Flags: dontcopy",
    "{sysnative}\WindowsPowerShell\v1.0\powershell.exe",
    "RegistryStringEquals(InprocPath, 'ThreadingModel', 'Both')",
    "RegistryStringEquals(VersionedInprocPath, 'RuntimeVersion', 'v4.0.30319')",
    "RegistryStringEquals(SolidEdgePath, 'Description'",
    "RegistryStringEquals(SolidEdgePath, 'Summary'",
    "RegistryDwordEquals(SolidEdgePath, 'Visible', 1)",
    "OutputBaseFilename=SE_HardwareInserterSetup-{#MyAppVersion}",
    "PrivilegesRequired=lowest",
    "UsePreviousAppDir=no",
    "#if VER < EncodeVer(6, 7, 3)",
    "RegistrationReadbackIsValid()",
    "InitializeSetup(): Boolean",
    "InitializeUninstall(): Boolean"
)
foreach ($fragment in $requiredFragments) {
    if ($installer.IndexOf($fragment, [StringComparison]::Ordinal) -lt 0) {
        throw "El instalador no contiene el fragmento contractual '$fragment'."
    }
}

if ($installer -match '(?i)CreateComObject') {
    throw 'El instalador no puede activar el add-in dentro del proceso Inno x86.'
}
if ($installer -match '(?im)^Root:\s+HKCU;' -or
    $installer -match '(?im)^Root:.*Flags:.*\b64bit\b') {
    throw 'El contrato debe usar raíces HKCU64; 64bit no es un flag válido de [Registry].'
}

$helperPath = Join-Path $PSScriptRoot 'installer-helper.ps1'
$uriOutput = Join-Path ([IO.Path]::GetTempPath()) ("se-hardware-uri-" + [Guid]::NewGuid().ToString('N') + '.txt')
$unicodePath = 'C:\Users\José Gómez\SE_HardwareInserter\SE_HardwareInserter.AddIn.dll'
try {
    & $helperPath -AssemblyPath $unicodePath -OutputPath $uriOutput
    $actualUri = [IO.File]::ReadAllText($uriOutput)
    $expectedUri = [Uri]::new($unicodePath).AbsoluteUri
    if ($actualUri -ne $expectedUri -or
        $actualUri -ne 'file:///C:/Users/Jos%C3%A9%20G%C3%B3mez/SE_HardwareInserter/SE_HardwareInserter.AddIn.dll') {
        throw "CodeBase Unicode inesperado: '$actualUri'."
    }
}
finally {
    if (Test-Path -LiteralPath $uriOutput) { [IO.File]::Delete($uriOutput) }
}

$fileEntries = [regex]::Matches(
    $installer,
    '(?m)^Source:\s+"\{#BuildOutputDir\}\\([^"]+)"')
$payloadNames = @($fileEntries | ForEach-Object { $_.Groups[1].Value })
$expectedPayload = @(
    "SE_HardwareInserter.AddIn.dll",
    "SE_HardwareInserter.Core.dll",
    "SE_HardwareInserter.SolidEdge.dll",
    "Interop.SolidEdge.dll"
)
$actualPayloadContract = ($payloadNames | Sort-Object) -join "`n"
$expectedPayloadContract = ($expectedPayload | Sort-Object) -join "`n"
if ($actualPayloadContract -ne $expectedPayloadContract) {
    throw "Payload inesperado: $($payloadNames -join ', ')."
}

if ($installer -match '(?m)^\[(?:Run|UninstallRun)\]\s*$' -or
    $installer -match '(?im)^Filename:.*RegAsm(?:\.exe)?' -or
    $installer -match '(?m)^Source:.*\\\*') {
    throw "El instalador no puede usar RegAsm ni comodines de payload."
}

foreach ($scriptName in @("register-addin.ps1", "unregister-addin.ps1")) {
    $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Raw
    if ($content -match '(?i)RegAsm(?:\.exe)?') {
        throw "$scriptName todavía depende de RegAsm."
    }
}

Write-Host "Contrato de registro e instalador coherente." -ForegroundColor Green
