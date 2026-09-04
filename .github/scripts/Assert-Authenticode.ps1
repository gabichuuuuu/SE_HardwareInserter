[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedSubject
)

$ErrorActionPreference = 'Stop'
foreach ($candidate in $Path) {
    $resolved = [IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "No existe el artefacto firmado '$resolved'."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $resolved
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate) {
        throw "Firma Authenticode inválida en '$resolved': $($signature.Status) $($signature.StatusMessage)"
    }

    $actualSubject = $signature.SignerCertificate.Subject
    $subjectComponents = @($actualSubject -split ',\s*')
    $subjectMatches =
        $actualSubject.Equals($ExpectedSubject, [StringComparison]::OrdinalIgnoreCase) -or
        ($subjectComponents | Where-Object {
            $_.Equals($ExpectedSubject, [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
    if (-not $subjectMatches) {
        throw "Firmante inesperado en '$resolved': '$actualSubject'; esperado '$ExpectedSubject'."
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "La firma de '$resolved' no contiene un timestamp Authenticode verificable."
    }

    Write-Host "Firma válida: $resolved [$($signature.SignerCertificate.Thumbprint)]" -ForegroundColor Green
}
