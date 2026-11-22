[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'
$programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
$windowsKitsBin = Join-Path $programFilesX86 'Windows Kits\10\bin'
if (-not (Test-Path -LiteralPath $windowsKitsBin -PathType Container)) {
    throw "No existe el directorio de Windows SDK '$windowsKitsBin'."
}

$candidates = @(
    Get-ChildItem -LiteralPath $windowsKitsBin -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [Version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
)
if ($candidates.Count -eq 0) {
    throw "No se encontró signtool.exe x64 en '$windowsKitsBin'."
}
$signTool = $candidates[0]

foreach ($candidate in $Path) {
    $resolved = [IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "No existe el artefacto firmado '$resolved'."
    }

    & $signTool verify /pa /all /v $resolved
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify /pa /all falló para '$resolved' con código $LASTEXITCODE."
    }
}

Write-Host "SignTool validó $($Path.Count) artefacto(s) mediante /pa /all." -ForegroundColor Green
