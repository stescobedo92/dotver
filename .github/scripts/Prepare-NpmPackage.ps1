param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$Repository,
    # checksums.txt produced by the publish-binaries job: "<asset>  <sha256>" per line.
    [Parameter(Mandatory = $true)]
    [string]$ChecksumsPath
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot "..\..\packaging\npm"
$output = Join-Path $PSScriptRoot "..\..\artifacts\npm"

if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item -Recurse -Force "$source\*" $output
# Tests are not part of the published package.
Remove-Item -Recurse -Force (Join-Path $output "test") -ErrorAction SilentlyContinue

# The installer verifies the downloaded binary against these digests, so they
# must come from the artifacts of this very run, never from a later download.
$checksums = [ordered]@{}
foreach ($line in Get-Content $ChecksumsPath) {
    if ($line -match '^(?<name>dotver-\S+)\s+(?<hash>[0-9a-fA-F]{64})$') {
        $checksums[$Matches.name] = $Matches.hash.ToLowerInvariant()
    }
}

$required = @('dotver-win-x64.exe', 'dotver-linux-x64', 'dotver-osx-arm64', 'dotver-osx-x64')
foreach ($asset in $required) {
    if (-not $checksums.Contains($asset)) {
        throw "Checksums file '$ChecksumsPath' has no SHA-256 for $asset; refusing to build an unverifiable package."
    }
}

$packageJsonPath = Join-Path $output "package.json"
$package = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$package.version = $Version
$package.repository.url = "https://github.com/$Repository.git"
$package.binaryChecksums = [pscustomobject]$checksums
$package | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath -Encoding utf8

foreach ($file in @('bin/dotver.js', 'bin/dotnet.js', 'scripts/install.mjs', 'scripts/lib.mjs')) {
    if (-not (Test-Path (Join-Path $output $file))) {
        throw "Package is missing $file (is it ignored by .gitignore?)."
    }
}
