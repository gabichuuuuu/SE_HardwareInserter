[CmdletBinding(DefaultParameterSetName = 'CodeBase')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'CodeBase')]
    [string]$AssemblyPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'CodeBase')]
    [string]$OutputPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Activation')]
    [switch]$Activate,

    [Parameter(Mandatory = $true, ParameterSetName = 'Activation')]
    [string]$Clsid
)

$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'CodeBase') {
    $fullPath = [IO.Path]::GetFullPath($AssemblyPath)
    $absoluteUri = [Uri]::new($fullPath).AbsoluteUri
    [IO.File]::WriteAllText(
        [IO.Path]::GetFullPath($OutputPath),
        $absoluteUri,
        [Text.Encoding]::ASCII)
    exit 0
}

if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
    throw 'La activación COM debe ejecutarse en un proceso de 64 bits.'
}

$instance = $null
try {
    $type = [Type]::GetTypeFromCLSID([Guid]$Clsid, $true)
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
