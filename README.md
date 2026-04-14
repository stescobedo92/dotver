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

## Examples

### Install the latest LTS and switch to it

```bash
$ dotver install lts
✔ Resolving version or channel
✔ Installing 8.0.412
✔ Applying automatic current/default
✔ Configuring shims, PATH and profiles

┌──────────────────┬────────────────────────────────────────────┐
│ Step             │ Value                                      │
├──────────────────┼────────────────────────────────────────────┤
│ Requested        │ lts                                        │
│ Resolved         │ 8.0.412                                    │
│ Reason           │ Channel 8.0 resolved to 8.0.412.           │
│ Status           │ Installed                                  │
│ Current          │ 8.0.412                                    │
│ Default          │ 8.0.412                                    │
│ Managed root     │ ~/.dotver/installs/8.0.412                 │
│ Shims            │ ~/.dotver/shims                            │
│ Persistent PATH  │ updated                                    │
│ Process PATH     │ updated                                    │
└──────────────────┴────────────────────────────────────────────┘
```

### List all detected SDKs

```bash
$ dotver list
╭──────────┬─────────┬─────────┬─────────┬────────┬──────────────────┬─────────────────────────────────╮
│ Version  │ Current │ Default │ Managed │ Direct │ Origin           │ SdkRoot                         │
├──────────┼─────────┼─────────┼─────────┼────────┼──────────────────┼─────────────────────────────────┤
│ 10.0.201 │ no      │ no      │ yes     │ yes    │ ManagedInstall   │ ~/.dotver/installs/10.0.201/sdk │
│ 8.0.412  │ yes     │ yes     │ yes     │ yes    │ ManagedInstall   │ ~/.dotver/installs/8.0.412/sdk  │
│ 6.0.428  │ no      │ no      │ no      │ no     │ OfficialInstall  │ /usr/share/dotnet/sdk/6.0.428   │
╰──────────┴─────────┴─────────┴─────────┴────────┴──────────────────┴─────────────────────────────────╯
```

### Switch version in a project using global.json

```bash
# Project has global.json with sdk.version = "10.0.201"
$ dotver install 10.0.201
$ dotver use 10.0.201

$ dotnet --version
10.0.201

# Back to default outside the project
$ cd ~
$ dotnet --version
8.0.412
```

### Create an alias and use it across projects

```bash
$ dotver alias preview 10.0.201
Alias preview -> 10.0.201

$ dotver use preview
Current -> 10.0.201

$ dotver alias
┌─────────┬──────────┐
│ Alias   │ Version  │
├─────────┼──────────┤
│ lts     │ 8.0.412  │
│ preview │ 10.0.201 │
└─────────┴──────────┘
```

### Check environment health

```bash
$ dotver doctor
╔══════════════════╤═══════════════╗
║ Doctor           │               ║
╠══════════════════╪═══════════════╣
║ SDKs detected    │ 3             ║
║ Managed SDKs     │ 2             ║
║ Direct-select    │ 2             ║
║ Current          │ 10.0.201      ║
║ Default          │ 8.0.412       ║
║ Aliases          │ 2             ║
║ Warning          │ None          ║
╚══════════════════╧═══════════════╝
```

### Install via npm (no .NET required)

```bash
npm install -g dotver
# or
npx dotver list
```

### Install via WinGet (Windows)

```bash
winget install DotVer.DotVer
```

## Operational model

`dotver` distinguishes between two types of inventory:

- `managed`: Isolated SDKs under `~/.dotver/installs/<version>`, directly selectable.
- `external`: SDKs found in official or custom system roots. These are detected and audited, but can only be used directly when their host root contains a single SDK.

This is intentional: an official root like `C:\Program Files\dotnet` may contain multiple SDKs, and the host `dotnet` decides which one to use based on `global.json` and the CLI's roll-forward rules.

## Automatic configuration

`dotver` automatically configures on first use:

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
