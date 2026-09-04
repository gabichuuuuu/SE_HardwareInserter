[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string]$StageDirectory
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$stage = [IO.Path]::GetFullPath($StageDirectory)
if (-not [IO.Directory]::Exists($repository)) { throw "No existe '$repository'." }
if (-not [IO.Directory]::Exists($stage)) { throw "No existe '$stage'." }

$metadataRoot = Join-Path $stage '_build-metadata'
[IO.Directory]::CreateDirectory($metadataRoot) | Out-Null
foreach ($name in @('Directory.Build.props', 'Directory.Packages.props', 'global.json')) {
    $source = Join-Path $repository $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        [IO.File]::Copy($source, (Join-Path $metadataRoot $name), $false)
    }
}

$projects = @(Get-ChildItem -LiteralPath $repository -Recurse -File -Filter '*.csproj' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })
foreach ($project in $projects) {
    $projectDirectory = $project.Directory.FullName
    $assets = Join-Path $projectDirectory 'obj\project.assets.json'
    if (-not (Test-Path -LiteralPath $assets -PathType Leaf)) {
        throw "Falta metadata restaurada '$assets'."
    }

    $relativeDirectory = [IO.Path]::GetRelativePath($repository, $projectDirectory)
    $destinationDirectory = Join-Path $metadataRoot $relativeDirectory
    [IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    [IO.File]::Copy($project.FullName, (Join-Path $destinationDirectory $project.Name), $false)
    [IO.File]::Copy($assets, (Join-Path $destinationDirectory 'project.assets.json'), $false)
}

if ($projects.Count -eq 0) { throw 'No se encontró ningún proyecto para el SBOM.' }
Write-Host "Metadata de build preparada para $($projects.Count) proyecto(s)." -ForegroundColor Green
