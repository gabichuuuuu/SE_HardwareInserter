[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

if ($Tag -notmatch '^v(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))$') {
    throw "El tag '$Tag' no tiene el formato estricto vMAJOR.MINOR.PATCH."
}

$Matches['version']
