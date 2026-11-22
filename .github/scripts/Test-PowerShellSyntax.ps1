[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'
$failures = New-Object 'System.Collections.Generic.List[string]'
$files = Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.ps1', '.psm1') }

foreach ($file in $files) {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref]$tokens,
        [ref]$errors)
    foreach ($error in $errors) {
        $failures.Add("$($file.FullName):$($error.Extent.StartLineNumber): $($error.Message)")
    }
}

if ($failures.Count -gt 0) {
    throw "Errores de sintaxis PowerShell:`n$($failures -join "`n")"
}

Write-Host "PowerShell válido: $($files.Count) archivo(s)." -ForegroundColor Green
