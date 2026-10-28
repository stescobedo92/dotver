# dotver

`dotver` is a .NET SDK version manager inspired by the ergonomics of `nvm` and `sdkman`, but focused on the `dotnet` ecosystem.

## Goals

- Interactive TUI/CLI with `Spectre.Console`.
- Cross-platform: Windows, Linux, and macOS.
- Detection of installed SDKs via official root, `DOTNET_ROOT`, `PATH`, and manually registered roots.
- Isolated installations in `~/.dotver/installs/<version>` using the official `dotnet-install` script. Cross-architecture installs live in `installs/<version>--<arch>` and are never auto-selected; runtime-only installs live in `~/.dotver/runtimes/`.
- Per-project SDK pins in `global.json` (`dotver local`), with one-command onboarding on any machine (`dotver ensure`).
- Shims to intercept `dotnet --info`, `dotnet --list-sdks`, and `dotnet --version`, plus one-off runs with a specific SDK (`dotver exec`) that do not change your global selection.
- Maintenance built in: update/EOL reporting (`outdated`/`upgrade`), health checks with repairs (`doctor --fix`), and disk cleanup (`prune`).
- Scriptable output (`--json`, stable exit codes) and shell completions for PowerShell, bash, zsh, and fish.
- Unit tests and pipelines for build/release.
- Packaging prepared for npm and manifest generation for WinGet.

## Main commands

```bash
dotver
dotver list [--managed-only] [--json]
dotver current [--json]
dotver explain [--json]      # every step of the selection chain and why it won or lost
dotver use 8.0.412
dotver use system            # clear the persisted current version
dotver deactivate            # same as `use system`
dotver default 10.0.201      # `default system` clears the default
dotver local 8.0.412 [--roll-forward latestFeature]   # pin this project in global.json
dotver local                 # show the effective pin
dotver local --clear         # remove this directory's pin (--parent for a parent pin)
dotver ensure [--use]        # install the SDK pinned by global.json if missing
dotver scan [PATH] [--ensure [--dry-run]] [--json]   # every pin in a tree; --ensure installs what is missing
dotver alias lts 8.0.412     # reserved names (system/current/default/latest) are rejected
dotver install 10.0.201 [--no-use] [--no-default]
dotver install 8.0
dotver install lts
dotver install 8.0.412 --arch arm64        # cross-arch: installs/<version>--<arch>
dotver install 8.0 --runtime aspnetcore    # runtime-only under ~/.dotver/runtimes
dotver info [8.0] [--json]                 # runtimes, workloads, architecture
dotver list-remote [--json]
dotver outdated [--all] [--managed-only] [--json]
dotver upgrade 8.0.400 [--prune-old]
dotver upgrade --all [--prune-old] [--json]
dotver exec 8.0 -- dotnet test   # run with one SDK, without changing state
dotver which [8.0] [--json]      # print the resolved dotnet path
dotver uninstall 8.0.412
dotver doctor [--fix] [--json]   # exits non-zero while issues remain
dotver prune [--keep-latest-per-channel --yes] [--json]  # dry-run unless --yes
dotver completions powershell|bash|zsh|fish
dotver register-root /custom/dotnet
```

Selection precedence for the active SDK is: `DOTVER_VERSION` (exact installed
version, set automatically by `exec`) > nearest `global.json` pin > persisted
`current` (`use`) > persisted `default` > system host. An unmatched
`DOTVER_VERSION` fails loudly instead of silently selecting another SDK.
`dotver explain` prints that chain step by step (selected, overridden,
unmatched, skipped) with the reason for each, which is the first thing to run
when "dotnet is picking the wrong SDK".

Inventory scans are cached under `~/.dotver/cache/` (fingerprint-validated,
24 h max age). Set `DOTVER_NO_CACHE=1` to disable the cache entirely.

JSON reports (`--json`, schema version 1) include absolute local paths.
Treat them as local diagnostics; do not paste them into public issues verbatim.

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
│ Installed SDK    │ 8.0.412                                    │
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

### Pin a project with global.json and onboard another machine

```bash
# Inside the repo: create/update global.json (other properties are preserved)
$ dotver local 10.0.201
Local -> 10.0.201
~/src/my-app/global.json

# The pin wins inside this directory tree, no `use` needed
$ dotnet --version
10.0.201

$ dotver local
Local -> 10.0.201
~/src/my-app/global.json

# On a fresh clone/machine, materialize the pinned SDK in one command
$ dotver ensure
# (add --use to also make it the persisted current version)

# Monorepo onboarding: see every pin, then install everything that is missing
$ dotver scan --ensure --dry-run     # lists "Would install: 9.0.100, ..." and changes nothing
$ dotver scan --ensure               # installs each distinct missing version once

# Back to current/default outside the project
$ cd ~
$ dotnet --version
8.0.412

# Remove the pin from this directory's global.json
$ cd ~/src/my-app && dotver local --clear
```

Pinning a version that is not installed yet is allowed (useful for shared
repos); `dotver ensure` installs exactly what the pin asks for, honoring its
`rollForward` (`dotver local 8.0.412 --roll-forward latestFeature`).

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

### Check environment health and repair it

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
║ Issues           │ 1             ║
╚══════════════════╧═══════════════╝
alias-missing Alias 'old' points to 6.0.999, which is not installed.
Run dotver doctor --fix to repair fixable issues.
# (exit code is 1 while issues remain)

$ dotver doctor --fix
Fixed: Removed alias 'old' (points to missing 6.0.999).
```

`doctor` reports dangling `current`/`default`/alias references, missing
registered roots, incomplete managed installs, missing shims, multi-SDK
roots, and a `global.json` pin no installed SDK satisfies. `--fix`
reconfigures shims/PATH/profiles and repairs the persisted config in one
save (previous config is backed up to `config.json.bak`). It refuses to
destroy config when the scan looks degraded — e.g. a registered root on a
disconnected drive — and reports `repair-skipped` instead.

### Run one command with a specific SDK

```bash
# Does not change current/default; the child gets DOTNET_ROOT and DOTVER_VERSION
$ dotver exec 8.0 -- dotnet test
$ dotver exec lts -- dotnet build -c Release

# Which dotnet host would run? (plain path, script-friendly)
$ dotver which
~/.dotver/installs/10.0.201/dotnet
$ dotver which 8.0
~/.dotver/installs/8.0.412/dotnet

# Back to the fallback chain (global.json, then default, then system)
$ dotver use system
# same as: dotver deactivate
```

### Keep SDKs updated and reclaim disk

```bash
$ dotver outdated
# Version   Channel   Latest    Status            Managed
# 8.0.400   8.0       8.0.412   update available  yes
# 6.0.428   6.0       6.0.428   end of life       yes

$ dotver upgrade --all --prune-old
# installs each channel's latest, repoints current/default/aliases,
# then deletes the old patch (never a stable-to-prerelease jump)

$ dotver prune
# managed disk usage per SDK (dry-run report)

$ dotver prune --keep-latest-per-channel
# still a dry run: shows what would be removed

$ dotver prune --keep-latest-per-channel --yes
# deletes older patches; current, default, alias targets and the current
# directory's global.json match (including rollForward) stay protected
```

### Scripting and CI

```bash
$ dotver list --json
$ dotver current --json
$ dotver outdated --json
$ dotver install 8.0.412 --no-use --no-default   # install without touching selection
$ dotver doctor --json                           # non-zero exit while issues remain
```

Every `--json` report carries `"schemaVersion": 1` and is written to raw
stdout (no terminal wrapping), so it is safe to pipe into `jq` or a CI parser.

### Shell completions

```powershell
# PowerShell: append the generated script to your profile
dotver completions powershell >> $PROFILE
```

```bash
# bash: add this line to ~/.bashrc
source <(dotver completions bash)

# zsh: save as _dotver in a directory on your fpath, or source it
source <(dotver completions zsh)

# fish
dotver completions fish > ~/.config/fish/completions/dotver.fish
```

Completions suggest commands plus installed versions and aliases for
version-taking commands (`use`, `local`, `exec`, `install`, `upgrade`, …).

### Install via npm (no .NET required)

```bash
npm install -g dotver
# or
npx dotver list
```

The package embeds the SHA-256 of every release binary; `postinstall` verifies
the download against it and refuses to install anything that does not match.

### Install via WinGet (Windows)

```bash
winget install DotVer.DotVer
```

## Operational model

`dotver` distinguishes between two types of inventory:

- `managed`: Isolated SDKs under `~/.dotver/installs/<version>`, directly selectable.
- `external`: SDKs found in official or custom system roots. These are detected and audited, but can only be used directly when their host root contains a single SDK.

This is intentional: an official root like `C:\Program Files\dotnet` may contain multiple SDKs, and the host `dotnet` decides which one to use based on `global.json` and the CLI's roll-forward rules.

Destructive or mutating maintenance only ever applies to managed SDKs:
`uninstall`, `upgrade`, and `prune` refuse to touch external installations,
and recursive deletes require the exact managed layout
(`~/.dotver/installs/<version>`, never through a symlink or junction).
`outdated` still reports external SDKs so end-of-life system installs stay
visible.

## Automatic configuration

`dotver` automatically configures on first use:

- shims in `~/.dotver/shims`
- persistent user `PATH`
- PowerShell profiles on Windows and supported profiles on Linux/macOS
- `current` and `default` when installing an SDK (opt out per install with
  `--no-use` and/or `--no-default`)

Script-oriented commands skip this bootstrap so they stay side-effect free:
`exec`, `which`, and `completions` never rewrite shims, `PATH`, or profiles.
The `dotnet` shim itself never bootstraps either: running `dotnet build` only
runs dotnet.

The bootstrap is conservative about your files:

- Set `DOTVER_NO_BOOTSTRAP=1` to keep dotver from touching `PATH`, shell
  profiles or the registry at all. Shims are still written under `~/.dotver`;
  source the snippet in `~/.dotver/env` yourself.
- Profiles are edited atomically, through symlinks (dotfile repos keep working),
  and `~/.config/fish/config.fish` is only touched when `~/.config/fish` exists.
- A dotver block you delete from a profile is not re-added: configured profiles
  are remembered in `~/.dotver/bootstrap.json`.
- On Windows, the user `PATH` keeps its `REG_EXPAND_SZ` kind and unexpanded
  `%VARIABLES%`; nothing is rewritten when the shims directory is already there.

`dotver register-root <path>` stores the absolute path of an existing directory;
relative entries (in the config, in `PATH` or in `DOTNET_ROOT`) are ignored by
the scanner so a cloned repository can never pose as a dotnet root.

On Windows, the current process cannot mutate the already-open parent shell; if the persistent PATH changes, simply open a new terminal.

## Documentation used

- `Spectre.Console` and `Spectre.Console.Cli` via Context7 for `CommandApp` structure, DI, prompts, and testing.
- Microsoft Learn for:
  - `global.json` and SDK selection.
  - `dotnet-install` script.
  - SDK installation detection.
  - packaging/publishing on WinGet.
