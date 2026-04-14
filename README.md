# dotver

`dotver` is a .NET SDK version manager inspired by the ergonomics of `nvm` and `sdkman`, but focused on the `dotnet` ecosystem.

## Goals

- Interactive TUI/CLI with `Spectre.Console`.
- Cross-platform: Windows, Linux, and macOS.
- Detection of installed SDKs via official root, `DOTNET_ROOT`, `PATH`, and manually registered roots.
- Isolated installations in `~/.dotver/installs/<version>` using the official `dotnet-install` script.
- Shims to intercept `dotnet --info`, `dotnet --list-sdks`, and `dotnet --version`.
- Unit tests and pipelines for build/release.
- Packaging prepared for npm and manifest generation for WinGet.

## Main commands

```bash
dotver
dotver list
dotver current
dotver use 8.0.412
dotver default 10.0.201
dotver alias lts 8.0.412
dotver install 10.0.201
dotver install 8.0
dotver install lts
dotver list-remote
dotver uninstall 8.0.412
dotver doctor
dotver register-root /custom/dotnet
```

## Operational model

`dotver` distinguishes between two types of inventory:

- `managed`: Isolated SDKs under `~/.dotver/installs/<version>`, directly selectable.
- `external`: SDKs found in official or custom system roots. These are detected and audited, but can only be used directly when their host root contains a single SDK.

This is intentional: an official root like `C:\Program Files\dotnet` may contain multiple SDKs, and the host `dotnet` decides which one to use based on `global.json` and the CLI's roll-forward rules.

## Automatic configuration

`dotver` now automatically configures:

- shims in `~/.dotver/shims`
- persistent user `PATH`
- PowerShell profiles on Windows and supported profiles on Linux/macOS
- `current` and `default` when installing an SDK

On Windows, the current process cannot mutate the already-open parent shell; if the persistent PATH changes, simply open a new terminal.

## Documentation used

- `Spectre.Console` and `Spectre.Console.Cli` via Context7 for `CommandApp` structure, DI, prompts, and testing.
- Microsoft Learn for:
  - `global.json` and SDK selection.
  - `dotnet-install` script.
  - SDK installation detection.
  - packaging/publishing on WinGet.

## CI/CD

- `ci.yml`: restore, build, and test on Windows, Linux, and macOS, including bootstrap and per-platform shim coverage.
- `release.yml`: publishes binaries per RID, GitHub release, npm package, and WinGet manifests as artifacts, including `osx-arm64` and `osx-x64`.

## Known limitations

- `dotver` can detect multi-version external SDKs, but cannot reliably force an exact version within an official multi-SDK root without relying on `global.json`. This behavior respects the actual semantics of the .NET CLI.
- Publishing to WinGet requires review of the WinGet ecosystem. The pipeline leaves the manifests ready and can be extended for automatic submission with the appropriate secrets.
