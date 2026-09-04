Set-StrictMode -Version 2.0

$script:RegistrationContract = [ordered]@{
    Clsid = '{DC87125A-6DBF-43A5-968B-2578CCBFC158}'
    CanonicalProgId = 'SE_HardwareInserter.AddIn'
    LegacyProgId = 'HardwareInserter.AddIn'
    ClassName = 'SE_HardwareInserter.AddIn.SE_HardwareInserterAddIn'
    AssemblyName = 'SE_HardwareInserter.AddIn'
    AssemblyVersion = '1.0.0.0'
    ManagedCategory = '{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}'
    AppId = '{7B7C6B2E-9C7D-4B2E-9E2B-6F5F6F5A1B3B}'
    CurrentInstallDirectoryName = 'SE_HardwareInserter'
    LegacyInstallDirectoryName = 'HardwareInserter'
}

function Get-SEHardwareInserterRegistrationContract {
    [CmdletBinding()]
    param()

    return [pscustomobject]$script:RegistrationContract
}

function Open-CurrentUser64 {
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::CurrentUser,
        [Microsoft.Win32.RegistryView]::Registry64)
}

function Set-RegistryValues {
    param(
        [Parameter(Mandatory = $true)]
        [Microsoft.Win32.RegistryKey]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [hashtable]$Values
    )

    $key = $Root.CreateSubKey($Path, $true)
    if ($null -eq $key) {
        throw "No se pudo crear HKCU\$Path."
    }

    try {
        foreach ($entry in $Values.GetEnumerator()) {
            $kind = if ($entry.Value -is [int]) {
                [Microsoft.Win32.RegistryValueKind]::DWord
            }
            else {
                [Microsoft.Win32.RegistryValueKind]::String
            }
            $key.SetValue([string]$entry.Key, $entry.Value, $kind)
        }
    }
    finally {
        $key.Dispose()
    }
}

function Remove-RegistryTree {
    param(
        [Parameter(Mandatory = $true)]
        [Microsoft.Win32.RegistryKey]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $Root.DeleteSubKeyTree($Path, $false)
}

function Get-RegistryKeySnapshot {
    param([Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Key)

    $values = [ordered]@{}
    foreach ($name in $Key.GetValueNames()) {
        $values[$name] = [pscustomobject]@{
            Kind = $Key.GetValueKind($name).ToString()
            Value = $Key.GetValue(
                $name,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }

    $children = [ordered]@{}
    foreach ($name in $Key.GetSubKeyNames()) {
        $child = $Key.OpenSubKey($name, $false)
        if ($null -eq $child) {
            throw "No se pudo leer la subclave '$($Key.Name)\$name'."
        }
        try {
            $children[$name] = Get-RegistryKeySnapshot -Key $child
        }
        finally {
            $child.Dispose()
        }
    }

    return [pscustomobject]@{
        Values = $values
        Children = $children
    }
}

function Get-RegistryTreeSnapshot {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $key = $Root.OpenSubKey($Path, $false)
    if ($null -eq $key) { return $null }
    try {
        return Get-RegistryKeySnapshot -Key $key
    }
    finally {
        $key.Dispose()
    }
}

function Restore-RegistryKeySnapshot {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Key,
        [Parameter(Mandatory = $true)][object]$Snapshot
    )

    foreach ($entry in $Snapshot.Values.GetEnumerator()) {
        $kind = [Microsoft.Win32.RegistryValueKind][Enum]::Parse(
            [Microsoft.Win32.RegistryValueKind],
            [string]$entry.Value.Kind)
        $Key.SetValue([string]$entry.Key, $entry.Value.Value, $kind)
    }
    foreach ($entry in $Snapshot.Children.GetEnumerator()) {
        $child = $Key.CreateSubKey([string]$entry.Key, $true)
        if ($null -eq $child) { throw "No se pudo restaurar '$($Key.Name)\$($entry.Key)'." }
        try {
            Restore-RegistryKeySnapshot -Key $child -Snapshot $entry.Value
        }
        finally {
            $child.Dispose()
        }
    }
}

function Restore-RegistryTreeSnapshot {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Root,
        [Parameter(Mandatory = $true)][string]$Path,
        [AllowNull()][object]$Snapshot
    )

    $Root.DeleteSubKeyTree($Path, $false)
    if ($null -eq $Snapshot) { return }

    $key = $Root.CreateSubKey($Path, $true)
    if ($null -eq $key) { throw "No se pudo restaurar HKCU\$Path." }
    try {
        Restore-RegistryKeySnapshot -Key $key -Snapshot $Snapshot
    }
    finally {
        $key.Dispose()
    }
}

function Get-ExpectedAssemblyDisplayName {
    $contract = Get-SEHardwareInserterRegistrationContract
    return "$($contract.AssemblyName), Version=$($contract.AssemblyVersion), Culture=neutral, PublicKeyToken=null"
}

function Get-ExpectedRegistrationValues {
    param([Parameter(Mandatory = $true)][string]$AssemblyPath)

    $fullPath = [IO.Path]::GetFullPath($AssemblyPath)
    $contract = Get-SEHardwareInserterRegistrationContract
    return [ordered]@{
        AssemblyPath = $fullPath
        CodeBase = ([Uri]$fullPath).AbsoluteUri
        AssemblyDisplayName = Get-ExpectedAssemblyDisplayName
        ClsidPath = "Software\Classes\CLSID\$($contract.Clsid)"
        CanonicalProgIdPath = "Software\Classes\$($contract.CanonicalProgId)"
        LegacyProgIdPath = "Software\Classes\$($contract.LegacyProgId)"
        SolidEdgePath = "Software\Solid Edge\Add-Ins\$($contract.Clsid)"
    }
}

function Register-SEHardwareInserterAddIn {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath
    )

    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
        throw 'El registro debe ejecutarse desde un proceso PowerShell de 64 bits.'
    }

    $expected = Get-ExpectedRegistrationValues -AssemblyPath $AssemblyPath
    if (-not (Test-Path -LiteralPath $expected.AssemblyPath -PathType Leaf)) {
        throw "No existe '$($expected.AssemblyPath)'."
    }

    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($expected.AssemblyPath)
    $contract = Get-SEHardwareInserterRegistrationContract
    if ($assemblyName.Name -ne $contract.AssemblyName -or
        $assemblyName.Version.ToString() -ne $contract.AssemblyVersion) {
        throw "El ensamblado debe ser $($contract.AssemblyName), Version=$($contract.AssemblyVersion)."
    }

    $paths = @(
        $expected.CanonicalProgIdPath,
        $expected.LegacyProgIdPath,
        $expected.ClsidPath,
        $expected.SolidEdgePath
    )
    $snapshots = [ordered]@{}
    $writeStarted = $false
    $snapshotRoot = Open-CurrentUser64
    try {
        foreach ($path in $paths) {
            $snapshots[$path] = Get-RegistryTreeSnapshot -Root $snapshotRoot -Path $path
        }
    }
    finally {
        $snapshotRoot.Dispose()
    }

    try {
        $writeRoot = Open-CurrentUser64
        try {
            $writeStarted = $true
            Set-RegistryValues $writeRoot $expected.CanonicalProgIdPath @{
                '' = $contract.ClassName
            }
            Set-RegistryValues $writeRoot "$($expected.CanonicalProgIdPath)\CLSID" @{
                '' = $contract.Clsid
            }
            Set-RegistryValues $writeRoot $expected.LegacyProgIdPath @{
                '' = $contract.ClassName
            }
            Set-RegistryValues $writeRoot "$($expected.LegacyProgIdPath)\CLSID" @{
                '' = $contract.Clsid
            }
            Set-RegistryValues $writeRoot $expected.ClsidPath @{
                '' = $contract.ClassName
            }

            $inprocValues = @{
                '' = 'mscoree.dll'
                'ThreadingModel' = 'Both'
                'Class' = $contract.ClassName
                'Assembly' = $expected.AssemblyDisplayName
                'RuntimeVersion' = 'v4.0.30319'
                'CodeBase' = $expected.CodeBase
            }
            Set-RegistryValues $writeRoot "$($expected.ClsidPath)\InprocServer32" $inprocValues
            Set-RegistryValues $writeRoot "$($expected.ClsidPath)\InprocServer32\$($contract.AssemblyVersion)" $inprocValues
            Set-RegistryValues $writeRoot "$($expected.ClsidPath)\ProgId" @{
                '' = $contract.CanonicalProgId
            }
            Set-RegistryValues $writeRoot "$($expected.ClsidPath)\Implemented Categories\$($contract.ManagedCategory)" @{}
            Set-RegistryValues $writeRoot $expected.SolidEdgePath @{
                'Description' = 'SE_HardwareInserter: inserción de tornillería y anchors desde librería local.'
                'Summary' = 'Add-in para inserción automática de hardware en ensamblajes de Solid Edge.'
                'Visible' = 1
            }
        }
        finally {
            $writeRoot.Dispose()
        }

        Test-SEHardwareInserterRegistration -AssemblyPath $expected.AssemblyPath -Activate | Out-Null
    }
    catch {
        if (-not $writeStarted) { throw }
        $originalError = $_
        $restoreFailures = New-Object 'System.Collections.Generic.List[string]'
        $restoreRoot = $null
        try {
            $restoreRoot = Open-CurrentUser64
            foreach ($path in $paths) {
                try {
                    Restore-RegistryTreeSnapshot -Root $restoreRoot -Path $path -Snapshot $snapshots[$path]
                }
                catch {
                    $restoreFailures.Add("HKCU\${path}: $($_.Exception.Message)")
                }
            }
        }
        catch {
            $restoreFailures.Add("No se pudo abrir o recorrer HKCU64 para restaurar: $($_.Exception.Message)")
        }
        finally {
            if ($null -ne $restoreRoot) { $restoreRoot.Dispose() }
        }
        if ($restoreFailures.Count -gt 0) {
            throw "El registro falló: $($originalError.Exception.Message). También falló la restauración: $($restoreFailures -join '; ')"
        }
        throw $originalError
    }
}

function Get-RegistryValue {
    param(
        [Parameter(Mandatory = $true)]
        [Microsoft.Win32.RegistryKey]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Name
    )

    $key = $Root.OpenSubKey($Path, $false)
    if ($null -eq $key) {
        throw "No existe HKCU\$Path."
    }
    try {
        return $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    }
    finally {
        $key.Dispose()
    }
}

function Assert-RegistryValue {
    param(
        [Microsoft.Win32.RegistryKey]$Root,
        [string]$Path,
        [string]$Name,
        [object]$Expected
    )

    $actual = Get-RegistryValue $Root $Path $Name
    if ([string]$actual -ne [string]$Expected) {
        throw "HKCU\$Path '$Name': esperado '$Expected', obtenido '$actual'."
    }
}

function Assert-RegistryKey {
    param(
        [Microsoft.Win32.RegistryKey]$Root,
        [string]$Path
    )

    $key = $Root.OpenSubKey($Path, $false)
    if ($null -eq $key) { throw "No existe HKCU\$Path." }
    $key.Dispose()
}

function Test-SEHardwareInserterRegistration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath,

        [switch]$Activate
    )

    $expected = Get-ExpectedRegistrationValues -AssemblyPath $AssemblyPath
    $contract = Get-SEHardwareInserterRegistrationContract
    $root = Open-CurrentUser64
    try {
        Assert-RegistryValue $root $expected.CanonicalProgIdPath '' $contract.ClassName
        Assert-RegistryValue $root "$($expected.CanonicalProgIdPath)\CLSID" '' $contract.Clsid
        Assert-RegistryValue $root $expected.LegacyProgIdPath '' $contract.ClassName
        Assert-RegistryValue $root "$($expected.LegacyProgIdPath)\CLSID" '' $contract.Clsid
        Assert-RegistryValue $root $expected.ClsidPath '' $contract.ClassName
        Assert-RegistryValue $root "$($expected.ClsidPath)\InprocServer32" '' 'mscoree.dll'
        Assert-RegistryValue $root "$($expected.ClsidPath)\InprocServer32" 'ThreadingModel' 'Both'
        Assert-RegistryValue $root "$($expected.ClsidPath)\InprocServer32" 'Class' $contract.ClassName
        Assert-RegistryValue $root "$($expected.ClsidPath)\InprocServer32" 'Assembly' $expected.AssemblyDisplayName
        Assert-RegistryValue $root "$($expected.ClsidPath)\InprocServer32" 'RuntimeVersion' 'v4.0.30319'
        Assert-RegistryValue $root "$($expected.ClsidPath)\InprocServer32" 'CodeBase' $expected.CodeBase
        $versionedInproc = "$($expected.ClsidPath)\InprocServer32\$($contract.AssemblyVersion)"
        Assert-RegistryValue $root $versionedInproc '' 'mscoree.dll'
        Assert-RegistryValue $root $versionedInproc 'ThreadingModel' 'Both'
        Assert-RegistryValue $root $versionedInproc 'Class' $contract.ClassName
        Assert-RegistryValue $root $versionedInproc 'Assembly' $expected.AssemblyDisplayName
        Assert-RegistryValue $root $versionedInproc 'RuntimeVersion' 'v4.0.30319'
        Assert-RegistryValue $root $versionedInproc 'CodeBase' $expected.CodeBase
        Assert-RegistryValue $root "$($expected.ClsidPath)\ProgId" '' $contract.CanonicalProgId
        Assert-RegistryKey $root "$($expected.ClsidPath)\Implemented Categories\$($contract.ManagedCategory)"
        Assert-RegistryValue $root $expected.SolidEdgePath 'Description' 'SE_HardwareInserter: inserción de tornillería y anchors desde librería local.'
        Assert-RegistryValue $root $expected.SolidEdgePath 'Summary' 'Add-in para inserción automática de hardware en ensamblajes de Solid Edge.'
        Assert-RegistryValue $root $expected.SolidEdgePath 'Visible' 1
    }
    finally {
        $root.Dispose()
    }

    if ($Activate) {
        $instance = $null
        try {
            $type = [Type]::GetTypeFromCLSID([Guid]$contract.Clsid, $true)
            $instance = [Activator]::CreateInstance($type)
            if ($null -eq $instance) {
                throw 'La activación COM devolvió null.'
            }
        }
        finally {
            if ($null -ne $instance -and [Runtime.InteropServices.Marshal]::IsComObject($instance)) {
                [void][Runtime.InteropServices.Marshal]::ReleaseComObject($instance)
            }
        }
    }

    return $true
}

function Unregister-SEHardwareInserterAddIn {
    [CmdletBinding()]
    param()

    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
        throw 'El desregistro debe ejecutarse desde un proceso PowerShell de 64 bits.'
    }

    $contract = Get-SEHardwareInserterRegistrationContract
    $root = Open-CurrentUser64
    try {
        Remove-RegistryTree $root "Software\Solid Edge\Add-Ins\$($contract.Clsid)"
        Remove-RegistryTree $root "Software\Classes\$($contract.CanonicalProgId)"
        Remove-RegistryTree $root "Software\Classes\$($contract.LegacyProgId)"
        Remove-RegistryTree $root "Software\Classes\CLSID\$($contract.Clsid)"
    }
    finally {
        $root.Dispose()
    }
}

Export-ModuleMember -Function @(
    'Get-SEHardwareInserterRegistrationContract',
    'Register-SEHardwareInserterAddIn',
    'Test-SEHardwareInserterRegistration',
    'Unregister-SEHardwareInserterAddIn'
)
