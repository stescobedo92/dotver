param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$Repository,
    [Parameter(Mandatory = $true)]
    [string]$PackageIdentifier,
    [Parameter(Mandatory = $false)]
    [string]$InstallerSha256 = ""
)

$manifestRoot = Join-Path $PSScriptRoot "..\..\artifacts\winget\$Version"
New-Item -ItemType Directory -Force -Path $manifestRoot | Out-Null

$installerUrl = "https://github.com/$Repository/releases/download/v$Version/dotver-win-x64-portable.zip"

@"
PackageIdentifier: $PackageIdentifier
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.9.0
"@ | Set-Content (Join-Path $manifestRoot "$PackageIdentifier.yaml")

@"
PackageIdentifier: $PackageIdentifier
PackageVersion: $Version
PackageLocale: en-US
Publisher: DotVer
PackageName: dotver
ShortDescription: Cross-platform .NET SDK version manager with Spectre.Console TUI.
License: MIT
ManifestType: defaultLocale
ManifestVersion: 1.9.0
"@ | Set-Content (Join-Path $manifestRoot "$PackageIdentifier.locale.en-US.yaml")

$sha256Line = if ($InstallerSha256) { "`n    InstallerSha256: $InstallerSha256" } else { "" }

@"
PackageIdentifier: $PackageIdentifier
PackageVersion: $Version
Installers:
  - Architecture: x64
    InstallerType: portable
    InstallerUrl: $installerUrl$sha256Line
    Commands:
      - dotver
InstallerSuccessCodes:
  - 0
ManifestType: installer
ManifestVersion: 1.9.0
"@ | Set-Content (Join-Path $manifestRoot "$PackageIdentifier.installer.yaml")
