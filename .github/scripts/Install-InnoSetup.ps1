[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory
)

$ErrorActionPreference = 'Stop'
$version = '6.7.3'
$downloadUri =
    'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expectedSha256 = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
$expectedSigner = 'CN=Pyrsys B.V.'
$timeoutMilliseconds = 120000
$destination = [IO.Path]::GetFullPath($InstallDirectory)

if (Test-Path -LiteralPath $destination) {
    throw "El directorio portable de Inno ya existe: '$destination'."
}

$bootstrapDirectory = Join-Path ([IO.Path]::GetTempPath()) (
    'SE_HardwareInserter-inno-bootstrap-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($bootstrapDirectory) | Out-Null
$installer = Join-Path $bootstrapDirectory "innosetup-$version.exe"

try {
    Invoke-WebRequest -Uri $downloadUri -OutFile $installer
    $actualSha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if ($actualSha256 -ne $expectedSha256) {
        throw "SHA-256 inesperado para Inno Setup: '$actualSha256'."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject.IndexOf(
            $expectedSigner,
            [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Firma Authenticode inesperada en el bootstrap oficial de Inno Setup."
    }

    $startArguments = @{
        FilePath = $installer
        ArgumentList = @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            '/SP-',
            '/NOICONS',
            '/PORTABLE=1',
            '/CURRENTUSER',
            "/DIR=$destination"
        )
        PassThru = $true
        WindowStyle = 'Hidden'
    }
    $process = Start-Process @startArguments
    if (-not $process.WaitForExit($timeoutMilliseconds)) {
        try {
            $process.Kill()
            $process.WaitForExit()
        }
        catch {
            throw "El bootstrap superó el timeout y no pudo terminarse: $($_.Exception.Message)"
        }
        throw "El bootstrap de Inno Setup superó $timeoutMilliseconds ms."
    }
    if ($process.ExitCode -ne 0) {
        throw "El bootstrap de Inno Setup devolvió $($process.ExitCode)."
    }
}
catch {
    if (Test-Path -LiteralPath $destination) {
        [IO.Directory]::Delete($destination, $true)
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $bootstrapDirectory) {
        [IO.Directory]::Delete($bootstrapDirectory, $true)
    }
}

$iscc = Join-Path $destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc -PathType Leaf)) {
    throw "La extracción portable no produjo '$iscc'."
}

# ISCC.exe no publica una FileVersion útil ni imprime la versión en /?. La versión
# queda ligada al asset inmutable is-6_7_3 mediante su URL y SHA-256 comprobado.

$compilerSignature = Get-AuthenticodeSignature -LiteralPath $iscc
if ($compilerSignature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
    $null -eq $compilerSignature.SignerCertificate -or
    $compilerSignature.SignerCertificate.Subject.IndexOf(
        $expectedSigner,
        [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Firma Authenticode inesperada en '$iscc'."
}

Write-Host "Inno Setup $version portable verificado en '$destination'." -ForegroundColor Green
return $iscc
