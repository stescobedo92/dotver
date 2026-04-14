param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$Repository
)

$source = Join-Path $PSScriptRoot "..\..\packaging\npm"
$output = Join-Path $PSScriptRoot "..\..\artifacts\npm"

if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item -Recurse -Force "$source\*" $output

$packageJsonPath = Join-Path $output "package.json"
$package = Get-Content $packageJsonPath | ConvertFrom-Json
$package.version = $Version
$package.repository.url = "https://github.com/$Repository.git"
$package | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath
