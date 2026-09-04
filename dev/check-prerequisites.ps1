<#
.SYNOPSIS
Comprueba los prerrequisitos locales de desarrollo y ejecucion del add-in.

.DESCRIPTION
Solo realiza lecturas del sistema. No instala SDK, componentes de Windows, Inno Setup ni Solid Edge.
Devuelve codigo 0 cuando todos los prerrequisitos estan disponibles y codigo 1 en caso contrario.
#>
[CmdletBinding()]
param(
    [ValidateSet("All", "Development", "Packaging", "Runtime")]
    [string]$Profile = "All"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Continue"
$checks = New-Object 'System.Collections.Generic.List[object]'

function Add-Check {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Component,

        [Parameter(Mandatory = $true)]
        [bool]$Available,

        [Parameter(Mandatory = $true)]
        [string]$Detail,

        [string]$Path = "",

        [bool]$Required = $true
    )

    $checks.Add([pscustomobject]@{
        Component = $Component
        Status = if ($Available) { "OK" } elseif ($Required) { "MISSING" } else { "OPTIONAL" }
        Detail = $Detail
        Path = $Path
    })
}

function ConvertTo-Version {
    param([AllowEmptyString()][string]$Value)

    if ($Value -match '(\d+\.\d+\.\d+(?:\.\d+)?)') {
        try {
            return [version]$Matches[1]
        }
        catch {
            return $null
        }
    }

    return $null
}

function Get-RegistryDefaultValue {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    try {
        if (Test-Path -LiteralPath $LiteralPath) {
            return (Get-Item -LiteralPath $LiteralPath).GetValue("")
        }
    }
    catch {
        return $null
    }

    return $null
}

function ConvertTo-ExecutablePath {
    param([AllowEmptyString()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Value.Trim())
    if ($expanded -match '^"([^"]+\.exe)"') {
        return $Matches[1]
    }

    if ($expanded -match '^(.*?\.exe)(?:\s|$)') {
        return $Matches[1]
    }

    return $expanded.Trim('"')
}

function Get-PeArchitecture {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::Open($LiteralPath, 'Open', 'Read', 'ReadWrite')
        $reader = New-Object System.IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            return "Unknown"
        }

        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            return "Unknown"
        }

        switch ($reader.ReadUInt16()) {
            0x8664 { return "x64" }
            0x014C { return "x86" }
            0xAA64 { return "ARM64" }
            default { return "Unknown" }
        }
    }
    catch {
        return "Unknown"
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        elseif ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

# SDK fijado por global.json: latestPatch permite 10.0.400-10.0.499.
$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $dotnetCommand) {
    Add-Check "SDK .NET 10.0.400" $false "dotnet no esta disponible en PATH." "" ($Profile -in @("All", "Development", "Packaging"))
}
else {
    $sdkLines = @(& $dotnetCommand.Source --list-sdks 2>$null)
    $sdkVersions = @(
        foreach ($line in $sdkLines) {
            if ($line -match '^(\d+\.\d+\.\d+)\s') {
                [version]$Matches[1]
            }
        }
    )
    $compatibleSdk = $sdkVersions |
        Where-Object { $_ -ge [version]'10.0.400' -and $_ -lt [version]'10.0.500' } |
        Sort-Object -Descending |
        Select-Object -First 1
    $installedText = if ($sdkVersions.Count -gt 0) {
        ($sdkVersions | ForEach-Object { $_.ToString(3) }) -join ", "
    }
    else {
        "ninguno"
    }
    Add-Check "SDK .NET 10.0.400" ($null -ne $compatibleSdk) "SDK instalados: $installedText. Requerido: 10.0.400-10.0.499." $dotnetCommand.Source ($Profile -in @("All", "Development", "Packaging"))
}

$is64BitOs = [Environment]::Is64BitOperatingSystem
$processArchitecture = if ([Environment]::Is64BitProcess) { "64-bit" } else { "32-bit" }
$osArchitecture = if ($is64BitOs) { "64-bit" } else { "32-bit" }
Add-Check "Arquitectura de Windows" $is64BitOs "Sistema operativo: $osArchitecture; proceso actual: $processArchitecture."

$frameworkKey = 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
$framework = Get-ItemProperty -LiteralPath $frameworkKey -ErrorAction SilentlyContinue
$releaseProperty = if ($null -ne $framework) { $framework.PSObject.Properties['Release'] } else { $null }
$versionProperty = if ($null -ne $framework) { $framework.PSObject.Properties['Version'] } else { $null }
$frameworkRelease = if ($null -ne $releaseProperty) { [int]$releaseProperty.Value } else { 0 }
$frameworkVersion = if ($null -ne $versionProperty) { [string]$versionProperty.Value } else { "no detectado" }
Add-Check ".NET Framework 4.8+" ($frameworkRelease -ge 528040) "Version: $frameworkVersion; Release: $frameworkRelease." $frameworkKey

$windowsRoot = if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) { "C:\Windows" } else { $env:SystemRoot }
$regAsmPath = Join-Path $windowsRoot 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
Add-Check "RegAsm 64-bit" (Test-Path -LiteralPath $regAsmPath -PathType Leaf) "Solo se usa para regenerar el golden; instalacion y desarrollo registran declarativamente." $regAsmPath $false

$programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
$innoCandidates = @()
$isccCommand = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $isccCommand) {
    $innoCandidates += $isccCommand.Source
}
foreach ($candidate in @(
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'),
    (Get-RegistryDefaultValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe'),
    (Get-RegistryDefaultValue 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe')
)) {
    $candidatePath = ConvertTo-ExecutablePath ([string]$candidate)
    if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
        $innoCandidates += $candidatePath
    }
}
$innoPath = $innoCandidates |
    Select-Object -Unique |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ($null -eq $innoPath) {
    Add-Check "Inno Setup 6.7.3+" $false "No se encontro ISCC.exe." "" ($Profile -in @("All", "Packaging"))
}
else {
    $innoProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($innoPath).ProductVersion
    $innoVersion = ConvertTo-Version ([string]$innoProductVersion)
    Add-Check "Inno Setup 6.7.3+" ($null -ne $innoVersion -and $innoVersion -ge [version]'6.7.3') "Version detectada: $innoProductVersion." $innoPath ($Profile -in @("All", "Packaging"))
}

$edgeCandidates = @()
foreach ($appPathKey in @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Edge.exe',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Edge.exe'
)) {
    $edgePath = ConvertTo-ExecutablePath ([string](Get-RegistryDefaultValue $appPathKey))
    if (-not [string]::IsNullOrWhiteSpace($edgePath)) {
        $edgeCandidates += $edgePath
    }
}
foreach ($classesRoot in @('HKLM:\SOFTWARE\Classes', 'HKCU:\SOFTWARE\Classes')) {
    $clsid = Get-RegistryDefaultValue (Join-Path $classesRoot 'SolidEdge.Application\CLSID')
    if (-not [string]::IsNullOrWhiteSpace([string]$clsid)) {
        $edgePath = ConvertTo-ExecutablePath ([string](Get-RegistryDefaultValue (Join-Path $classesRoot "CLSID\$clsid\LocalServer32")))
        if (-not [string]::IsNullOrWhiteSpace($edgePath)) {
            $edgeCandidates += $edgePath
        }
    }
}
foreach ($programFilesRoot in @($env:ProgramFiles, $programFilesX86)) {
    if ([string]::IsNullOrWhiteSpace($programFilesRoot)) {
        continue
    }

    $siemensRoot = Join-Path $programFilesRoot 'Siemens'
    foreach ($directory in @(Get-ChildItem -LiteralPath $siemensRoot -Directory -Filter 'Solid Edge*' -ErrorAction SilentlyContinue)) {
        $edgeCandidates += (Join-Path $directory.FullName 'Program\Edge.exe')
    }
}
$solidEdgePath = $edgeCandidates |
    Select-Object -Unique |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ($null -eq $solidEdgePath) {
    Add-Check "Solid Edge x64" $false "No se encontro Edge.exe en registro ni rutas estandar." "" ($Profile -in @("All", "Runtime"))
}
else {
    $edgeArchitecture = Get-PeArchitecture $solidEdgePath
    $edgeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($solidEdgePath).ProductVersion
    Add-Check "Solid Edge x64" ($edgeArchitecture -eq 'x64') "Version: $edgeVersion; arquitectura PE: $edgeArchitecture." $solidEdgePath ($Profile -in @("All", "Runtime"))
}

Write-Host "Prerrequisitos de SE_HardwareInserter (perfil: $Profile)" -ForegroundColor Cyan
$checks | Format-Table -AutoSize -Wrap

$missing = @($checks | Where-Object { $_.Status -eq 'MISSING' })
if ($missing.Count -gt 0) {
    Write-Host "$($missing.Count) prerrequisito(s) pendiente(s). No se instalo ni modifico nada." -ForegroundColor Yellow
    exit 1
}

Write-Host "Todos los prerrequisitos requeridos por el perfil estan disponibles. No se instalo ni modifico nada." -ForegroundColor Green
exit 0
