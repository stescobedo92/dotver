# dotver

`dotver` is a .NET SDK version manager inspired by the ergonomics of `nvm` and `sdkman`, but focused on the `dotnet` ecosystem.

## Goals

- Interactive TUI/CLI with `Spectre.Console`.
- Cross-platform: Windows, Linux, and macOS.
- Detection of installed SDKs via official root, `DOTNET_ROOT`, `PATH`, and manually registered roots.
- Isolated installations in `~/.dotver/installs/<version>` using the official `dotnet-install` script, from a local archive on air-gapped machines (`install --from`), or through a corporate mirror (`mirror set`). Cross-architecture installs live in `installs/<version>--<arch>` and are never auto-selected; runtime-only installs live in `~/.dotver/runtimes/`.
- Per-project SDK pins in `global.json` (`dotver local`), one-command onboarding on any machine (`dotver ensure`), and monorepo-wide onboarding (`dotver scan --ensure`).
- A readable explanation of which SDK is active and why (`dotver explain`).
- Shims to intercept `dotnet --info`, `dotnet --list-sdks`, and `dotnet --version`, plus one-off runs with a specific SDK (`dotver exec`), matrix runs across SDKs (`dotver each`), and pinned interactive shells (`dotver shell`) that never change your global selection.
- Global tools kept in sync across SDKs (`dotver tools`) and a `post-install` hook.
- Maintenance built in: update/EOL reporting (`outdated`/`upgrade`), security audits against the official release metadata (`audit`), health checks with repairs (`doctor --fix`), and disk cleanup (`prune`).
- IDE and CI integration: `env --vscode`, `env --github`, and shell exports for `eval`.
- Self-contained lifecycle: `self-update` from verified GitHub releases and `self-uninstall`.
- Scriptable output (`--json`, stable exit codes) and shell completions for PowerShell, bash, zsh, and fish.
- Unit tests and pipelines for build/release.
- Packaging prepared for npm and manifest generation for WinGet.

## Main commands

```bash
# Inventory and selection
dotver                       # interactive dashboard (same as `dotver dashboard`)
dotver list [--managed-only] [--json]      # alias: ls
dotver current [--json]
dotver explain [--json]      # every step of the selection chain and why it won or lost
dotver which [8.0] [--json]  # print the resolved dotnet path
dotver info [8.0] [--json]   # runtimes, workloads, architecture
dotver use 8.0.412
dotver use system            # clear the persisted current version
dotver deactivate            # same as `use system`
dotver default 10.0.201      # `default system` clears the default
dotver alias lts 8.0.412     # reserved names (system/current/default/latest) are rejected
dotver unalias lts

# Per-project pins
dotver local 8.0.412 [--roll-forward latestFeature]   # pin this project in global.json (alias: pin)
dotver local                 # show the effective pin
dotver local --clear         # remove this directory's pin (--parent for a parent pin)
dotver ensure [PATH] [--use]           # install the SDK pinned by global.json if missing (alias: sync)
dotver ensure --recursive [PATH]       # install every pin under a tree
dotver scan [PATH] [--ensure [--dry-run]] [--json]   # every pin in a tree; --ensure installs what is missing

# Installing
dotver install 10.0.201 [--no-use] [--no-default] [--no-tools]
dotver install 8.0
dotver install lts
dotver install 8.0.412 --arch arm64        # cross-arch: installs/<version>--<arch>
dotver install 8.0 --runtime aspnetcore    # runtime-only under ~/.dotver/runtimes
dotver install 8.0.412 --from ./dotnet-sdk-8.0.412-linux-x64.tar.gz --sha512 <hex>   # offline, digest required
dotver list-remote [--json]                # alias: ls-remote
dotver mirror show | set --install-script <url> --releases-index <url> | clear      # corporate mirror
dotver tools add dotnet-ef [--version 8.0.0]   # global tools re-installed after every install
dotver tools list | remove <id> | sync [token]

# Running with a specific SDK (nothing persists)
dotver exec 8.0 -- dotnet test                                   # one command
dotver each 8.0,9.0,10.0 [--parallel] [--json] -- dotnet test    # the same command across several SDKs
dotver shell 8.0 [--shell pwsh|bash|zsh]                         # interactive shell pinned to one SDK
dotver env [8.0] [--shell powershell|bash|zsh|fish|cmd]          # print exports for eval
dotver env [8.0] --github                                        # append to GITHUB_ENV / GITHUB_PATH
dotver env [8.0] --vscode [--dir <path>]                         # write .vscode/settings.json

# Maintenance
dotver outdated [--all] [--managed-only] [--json]
dotver audit [--all] [--managed-only] [--json]   # SDKs missing security fixes: exit 1 vulnerable, 2 unverified
dotver upgrade 8.0.400 [--prune-old]
dotver upgrade --all [--prune-old] [--json]
dotver uninstall 8.0.412
dotver doctor [--fix] [--json]   # exits non-zero while issues remain
dotver prune [--keep-latest-per-channel --yes] [--json]  # dry-run unless --yes
dotver register-root /custom/dotnet
dotver completions powershell|bash|zsh|fish

# dotver itself
dotver self-update [--check] [--version 0.4.0] [--allow-unofficial-source]   # verified GitHub release
dotver self-uninstall [--yes] [--keep-sdks]      # undo shims, profiles, PATH, registry and ~/.dotver
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

### Exit codes

Commands meant to gate a script or a CI job return a stable non-zero code:

| Command | Non-zero when |
|---|---|
| `scan`, `scan --ensure --dry-run` | 1 while any pin is missing or the scan was truncated |
| `ensure --recursive` | 1 when a pin could not be installed |
| `doctor` | 1 while issues remain |
| `audit` | 1 if any SDK is vulnerable, 2 if any SDK could not be verified |
| `each` | 1 if any SDK's command failed, 2 on usage errors, 130 when interrupted with Ctrl+C |
| `self-uninstall --yes` | 1 if something could not be removed |

## Examples

### Install the latest LTS and switch to it

```bash
$ dotver install lts
✔ Resolving version or channel
✔ Installing 8.0.412
✔ Applying automatic current/default
✔ Syncing global tools
✔ Configuring shims, PATH and profiles

┌───────────────────┬────────────────────────────────────────────┐
│ Step              │ Value                                      │
├───────────────────┼────────────────────────────────────────────┤
│ Requested         │ lts                                        │
│ Resolved          │ 8.0.412                                    │
│ Reason            │ Channel 8.0 resolved to 8.0.412.           │
│ Status            │ Installed                                  │
│ Installed SDK     │ 8.0.412                                    │
│ Current           │ 8.0.412                                    │
│ Default           │ 8.0.412                                    │
│ Managed root      │ ~/.dotver/installs/8.0.412                 │
│ Global tools      │ 2 synced, 0 failed                         │
│ post-install hook │ ran                                        │
│ Shims             │ ~/.dotver/shims                            │
│ Persistent PATH   │ updated                                    │
│ Process PATH      │ updated                                    │
└───────────────────┴────────────────────────────────────────────┘
```

The `Global tools` and `post-install hook` rows only appear when the SDK is
new and you have recorded tools or a hook (see
[Keep global tools across SDKs](#keep-global-tools-across-sdks)).

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

### Explain which SDK is active and why

`dotnet --version` prints a number; `dotver explain` prints the decision.
Every source in the precedence chain gets a row with its outcome:
`selected` (the winner), `overridden` (would have matched, but a
higher-priority source won), `unmatched` (set, but no installed SDK
satisfies it), `skipped` (not set) or `error`.

```bash
$ cd ~/src/my-app        # has a global.json pinning 8.0.412 with rollForward latestPatch
$ dotver explain
┌────────────────┬───────────────────────────┬────────────┬──────────────────────────────────────────────────────────────┐
│ Step           │ Value                     │ Outcome    │ Detail                                                       │
├────────────────┼───────────────────────────┼────────────┼──────────────────────────────────────────────────────────────┤
│ DOTVER_VERSION │ -                         │ skipped    │ Not set (dotver exec sets it for its child process).         │
│ global.json    │ ~/src/my-app/global.json  │ selected   │ Pin 8.0.412 (rollForward: latestPatch) matched installed     │
│                │                           │            │ 8.0.427.                                                     │
│ current        │ -                         │ skipped    │ Not set (dotver use).                                        │
│ default        │ -                         │ skipped    │ Not set (dotver default).                                    │
│ system         │ /usr/share/dotnet/dotnet  │ overridden │ dotnet host found with SDK 10.0.403. A higher-priority       │
│                │                           │            │ source already won.                                          │
└────────────────┴───────────────────────────┴────────────┴──────────────────────────────────────────────────────────────┘
Result: 8.0.427 (Selected by global.json (8.0.412))
```

Outside any project the same command shows the fallback to the system host:

```bash
$ cd ~ && dotver explain
...
│ system         │ /usr/share/dotnet/dotnet  │ selected   │ dotnet host found with SDK 10.0.403.                         │
Result: 10.0.403 (Selected by system fallback)
```

With `--json` the report carries `winner` (`source`, `version`, `reason`,
`hostRoot`, `dotnetPath`), the ordered `steps` array (`source`, `value`,
`outcome`, `detail`, `winner`) and `error`:

```bash
$ dotver explain --json | jq -r '.winner.source + " -> " + .winner.version'
global.json -> 8.0.427
$ dotver explain --json | jq -r '.steps[] | select(.outcome == "unmatched") | .detail'
```

A typical surprise this catches: a stale `DOTVER_VERSION` exported in a
profile shows up as `unmatched` or `selected` in the first row, long before
you suspect `global.json`.

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

### Scan a monorepo and install every missing pin

`scan` walks a tree, reads every `global.json` and tells you which pins an
installed SDK already satisfies. It exits with 1 while anything is missing,
so it doubles as a CI gate:

```bash
$ cd ~/src/monorepo
$ dotver scan
┌───────────────────┬──────────┬─────────┬───────────┐
│ Project           │ Pinned   │ Matched │ Status    │
├───────────────────┼──────────┼─────────┼───────────┤
│ global.json       │ 8.0.412  │ 8.0.427 │ satisfied │
│ tools/global.json │ 10.0.100 │ -       │ missing   │
└───────────────────┴──────────┴─────────┴───────────┘
$ echo $?
1
```

Add `--ensure` to install what is missing, or `--ensure --dry-run` to see the
plan first. Each distinct version is installed once even if ten projects pin
it, and the plan never touches `current`/`default`:

```bash
$ dotver scan --ensure --dry-run
┌───────────────────┬──────────┬─────────┬───────────┐
│ Project           │ Pinned   │ Matched │ Status    │
├───────────────────┼──────────┼─────────┼───────────┤
│ global.json       │ 8.0.412  │ 8.0.427 │ satisfied │
│ tools/global.json │ 10.0.100 │ -       │ missing   │
└───────────────────┴──────────┴─────────┴───────────┘
Would install: 10.0.100 (dry run: drop --dry-run to install)

$ dotver scan --ensure          # installs 10.0.100, then re-reports; exit 0 when every pin is satisfied
$ dotver scan ./services --json # scan a sub-tree; `wouldInstall` lists the plan
```

The JSON report lists every pin (`filePath`, `requestedVersion`,
`rollForward`, `matchedVersion`, `satisfied`, `invalid`), `errors` and a
`truncated` flag for very large trees. With `--ensure --dry-run` it adds the
`wouldInstall` plan; with `--ensure` it adds `installedVersions`, what was
actually installed. `dotver ensure --recursive` does the same install without
the dry-run option.

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

$ dotver unalias preview
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

### Audit installed SDKs for missing security fixes

`audit` compares every detected SDK against the official release metadata
and lists the ones that miss a published security fix, with the CVEs and the
patch that closes them. Unknown metadata never passes as clean: the command
exits 1 when something is vulnerable and 2 when something could not be
verified, so an offline CI runner fails instead of reporting a false green.

```bash
$ dotver audit
Audited 3 SDK(s): 1 vulnerable, 0 unknown.
┌──────────┬─────────┬────────────┬──────────┬──────┐
│ Version  │ Channel │ Status     │ Fixed in │ CVEs │
├──────────┼─────────┼────────────┼──────────┼──────┤
│ 10.0.301 │ 10.0    │ vulnerable │ 10.0.303 │ 79   │
└──────────┴─────────┴────────────┴──────────┴──────┘
10.0.301 fixed by 10.0.303: CVE-2026-102870, CVE-2026-105016, CVE-2026-111497, ...
Run dotver upgrade --all to move managed SDKs to the fixed releases.
$ echo $?
1

$ dotver audit --all             # also list the SDKs that are fine
$ dotver audit --managed-only    # ignore system installs you cannot upgrade with dotver
$ dotver audit --json | jq -r '.sdks[] | select(.vulnerable) | "\(.version) -> \(.fixedIn)"'
10.0.301 -> 10.0.303
```

Each JSON entry carries `version`, `channel`, `isManaged`, `hostRoot`,
`metadataKnown`, `vulnerable`, `fixedIn` and `cveIds`. `audit` always reads
the official Microsoft index, even when a mirror is configured.

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

### Run the same command across several SDKs

`each` is `exec` for a matrix: one token per SDK (versions, channels or
aliases), the child command after `--`, and a header per run. Everything
after `--` belongs to the child, so its own flags are never parsed by
dotver.

```bash
$ dotver each 8.0,9.0,10.0 -- dotnet test
=== 8.0 => 8.0.412 (exit 0, 12345 ms) ===
Passed!  - Failed: 0, Passed: 142, Skipped: 0, Total: 142
=== 9.0 => 9.0.108 (exit 0, 11980 ms) ===
Passed!  - Failed: 0, Passed: 142, Skipped: 0, Total: 142
=== 10.0 => 10.0.403 (exit 1, 13102 ms) ===
Failed!  - Failed: 1, Passed: 141, Skipped: 0, Total: 142
$ echo $?
1

$ dotver each 8.0,10.0 --parallel -- dotnet build -c Release   # run concurrently, output grouped per SDK
$ dotver each lts,latest --json -- dotnet --version              # one entry per SDK with captured stdout/stderr
```

The JSON report has `allSucceeded` and one entry per token (`token`,
`version`, `exitCode`, `durationMs`, `error`, `stdout`, `stderr`). A token
that resolves to no installed SDK is reported as `unresolved` with its
`error` and fails the run instead of being skipped.

### Open a shell pinned to one SDK

When one command is not enough, open a sub-shell with the SDK first on
`PATH`. Nothing is persisted: exit the shell and your previous selection is
back.

```bash
$ dotver shell 8.0
# a new shell starts with DOTNET_ROOT, DOTVER_VERSION and DOTVER_SHELL=8.0.412
$ dotnet --version
8.0.412
$ exit

$ dotver shell 9.0 --shell pwsh   # pick the shell explicitly; defaults to $SHELL (Unix) or pwsh/powershell (Windows)
```

Use `DOTVER_SHELL` in your prompt to show that a pinned shell is active.

### Export the SDK environment to another process

`env` prints the exports (`DOTNET_ROOT`, `DOTVER_VERSION`, `PATH`) for the
active selection or a token, in the syntax of your shell, so you can `eval`
them in a script or a Makefile:

```bash
$ dotver env 8.0 --shell bash
export DOTNET_ROOT='/home/me/.dotver/installs/8.0.412'
export DOTVER_VERSION='8.0.412'
export PATH='/home/me/.dotver/installs/8.0.412:'"$PATH"

$ eval "$(dotver env 8.0 --shell bash)" && dotnet --version
8.0.412
```

```powershell
PS> dotver env            # PowerShell syntax is the default on Windows
$env:DOTNET_ROOT = 'C:\Users\me\.dotver\installs\10.0.201'
$env:DOTVER_VERSION = '10.0.201'
$env:PATH = 'C:\Users\me\.dotver\installs\10.0.201;' + $env:PATH

PS> dotver env 8.0 | Invoke-Expression
```

Supported values for `--shell` are `powershell`, `bash`, `zsh`, `fish` and
`cmd`. In GitHub Actions, `--github` appends the same variables to
`GITHUB_ENV` and the host directory to `GITHUB_PATH`, so every later step of
the job uses that SDK:

```yaml
- run: dotver install 8.0 --no-use --no-default
- run: dotver env 8.0 --github
- run: dotnet --version   # 8.0.x from here on
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
$ dotver explain --json                          # winner + every step of the chain
$ dotver outdated --json
$ dotver audit --json                            # exit 1 vulnerable, 2 unverified
$ dotver scan --ensure --dry-run --json          # exit 1 while a pin is missing; `wouldInstall` is the plan
$ dotver each 8.0,9.0 --json -- dotnet test      # one entry per SDK
$ dotver tools list --json
$ dotver mirror show --json
$ dotver self-update --check --json
$ dotver install 8.0.412 --no-use --no-default   # install without touching selection
$ dotver doctor --json                           # non-zero exit while issues remain
```

Every `--json` report carries `"schemaVersion": 1` and is written to raw
stdout (no terminal wrapping), so it is safe to pipe into `jq` or a CI parser.

A GitHub Actions job that fails when a pin is missing and then builds with
exactly the pinned SDKs:

```yaml
- run: npm install -g dotver
- run: dotver scan --ensure --dry-run      # fail fast and show what is missing
- run: dotver scan --ensure                # install every distinct pinned version once
- run: dotver audit                        # fail on SDKs missing security fixes
- run: dotver each 8.0,9.0 -- dotnet test
```

### Offline installs, mirrors and proxies

Air-gapped machine? Download the official SDK archive and its published
SHA-512 elsewhere, then install it without any network access. The digest is
mandatory, the archive is extracted into a staging directory with zip-slip
and link protection, validated as an SDK layout, and only then moved into
`~/.dotver/installs/<version>`:

```bash
$ dotver install 8.0.412 --from ./dotnet-sdk-8.0.412-linux-x64.tar.gz --sha512 <hex from the download page>
$ dotver install 8.0.412 --from .\dotnet-sdk-8.0.412-win-x64.zip --sha256 <hex>
```

`--from` needs an exact version (the archive cannot resolve a channel), the
digest must match before anything is extracted, and the usual `--no-use`,
`--no-default` and `--no-tools` flags apply.

Behind a corporate mirror, point dotver at it. Only plain `https` URLs are
accepted (no credentials, query or fragment) and only those hosts join the
download allow-list; on Windows the mirrored `dotnet-install.ps1` must still
carry a valid Microsoft Authenticode signature. `dotver audit` always uses
the official release index.

```bash
$ dotver mirror show
┌─────────────────────────────┬───────────────────────────────────────────────────────────────────────────────┐
│ Source                      │ URL                                                                           │
├─────────────────────────────┼───────────────────────────────────────────────────────────────────────────────┤
│ Install script (PowerShell) │ https://dot.net/v1/dotnet-install.ps1                                         │
│ Install script (bash)       │ https://dot.net/v1/dotnet-install.sh                                          │
│ Releases index              │ https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json │
│ Extra allowed hosts         │ (none)                                                                        │
└─────────────────────────────┴───────────────────────────────────────────────────────────────────────────────┘
Official Microsoft sources (no mirror configured).

$ dotver mirror set --install-script https://mirror.corp.example/dotnet/scripts \
                    --releases-index https://mirror.corp.example/dotnet/releases-index.json
Mirror saved.
┌─────────────────────────────┬───────────────────────────────────────────────────────────────┐
│ Source                      │ URL                                                           │
├─────────────────────────────┼───────────────────────────────────────────────────────────────┤
│ Install script (PowerShell) │ https://mirror.corp.example/dotnet/scripts/dotnet-install.ps1 │
│ Install script (bash)       │ https://mirror.corp.example/dotnet/scripts/dotnet-install.sh  │
│ Releases index              │ https://mirror.corp.example/dotnet/releases-index.json        │
│ Extra allowed hosts         │ mirror.corp.example                                           │
└─────────────────────────────┴───────────────────────────────────────────────────────────────┘
Mirror active. The install script is still signature-checked on Windows; audit always uses the official index.

$ dotver mirror set --install-script http://mirror.corp.example/dotnet
Error: The install script base URL must be an absolute https URL without credentials, query or fragment; got 'http://mirror.corp.example/dotnet'.

$ dotver mirror clear
Mirror cleared; downloads use the official Microsoft sources again.
```

`--install-script` is the directory that contains `dotnet-install.ps1` and
`dotnet-install.sh`; `--releases-index` is the full URL of a mirrored
`releases-index.json`. Either flag can be set alone; the other keeps its
current value. `mirror show --json` reports `mirrored`, the three effective
URLs and `extraAllowedHosts`.

Proxies: `HTTPS_PROXY`, `HTTP_PROXY` and `NO_PROXY` (and the Windows system
proxy) are honored for every download.

### Keep global tools across SDKs

Global tools (`dotnet tool install -g`) are restored against the runtime of
the SDK that installed them, so switching SDKs often leaves `dotnet-ef` or
`dotnet-format` broken. Record them once and dotver re-installs them with
every SDK it installs, like `default-packages` in nvm:

```bash
$ dotver tools add dotnet-ef --version 8.0.0
Tool recorded: dotnet-ef 8.0.0. It is installed after every dotver install; run dotver tools sync to install it now.
$ dotver tools add dotnet-format
Tool recorded: dotnet-format (latest). It is installed after every dotver install; run dotver tools sync to install it now.

$ dotver tools list
┌───────────────┬─────────┐
│ Tool          │ Version │
├───────────────┼─────────┤
│ dotnet-ef     │ 8.0.0   │
│ dotnet-format │ latest  │
└───────────────┴─────────┘

$ dotver install 9.0            # ... then runs `dotnet tool update --global` for each recorded tool
$ dotver tools sync 8.0         # do it now for an already installed SDK
$ dotver tools sync             # same, for the active selection

$ dotver tools remove dotnet-format
Tool removed from the manifest: dotnet-format (the installed tool itself is untouched).
```

The manifest lives in `~/.dotver/tools.json` (`tools list --json` prints it
with `filePath` and the `tools` array). `tools sync` tries `dotnet tool
update --global` first and falls back to `install`; `--no-tools` skips the
sync for one install, and failures are reported per tool without failing the
install.

Hooks: an executable `~/.dotver/hooks/post-install` (on Windows
`post-install.ps1`, `.cmd`, `.bat` or `.exe`) runs after every new SDK
install with `DOTVER_EVENT`, `DOTVER_VERSION`, `DOTNET_ROOT`,
`DOTVER_SDK_ROOT` and `DOTVER_HOME` in its environment. Its exit code and
output are reported; a failing hook never aborts the install.

```bash
# ~/.dotver/hooks/post-install  (chmod +x)
#!/usr/bin/env sh
echo "installed $DOTVER_VERSION at $DOTNET_ROOT"
"$DOTNET_ROOT/dotnet" workload install wasm-tools
```

```powershell
# ~/.dotver/hooks/post-install.ps1  (runs with pwsh -NoProfile)
Write-Host "installed $env:DOTVER_VERSION at $env:DOTNET_ROOT"
& "$env:DOTNET_ROOT\dotnet.exe" workload install wasm-tools
```

### Point your IDE at the selected SDK

Shims only reach terminals; an editor started from the desktop still uses the
system `dotnet`. For VS Code, write the SDK host into the project settings:

```bash
$ dotver env --vscode            # uses the active selection (global.json, current, ...)
$ dotver env 8.0 --vscode --dir ./src/MyApp
VS Code configured: ./src/MyApp/.vscode/settings.json (created)
  dotnet.dotnetPath = ~/.dotver/installs/8.0.412
  omnisharp.dotnetPath = ~/.dotver/installs/8.0.412
Reload the VS Code window so the C# extension picks up the new SDK. For Rider or Visual Studio launch the IDE from the SDK instead: dotver exec <version> -- rider64 .
```

Existing settings are preserved and the file is written atomically; JSONC
comments cannot be kept and are reported when dropped (`commentsDropped` in
the `--json` report, next to `filePath`, `created`, `hostRoot` and `keys`).
Rider and Visual Studio keep the .NET CLI path as an IDE setting rather than
a project file, so launch them with the SDK already selected instead:

```bash
$ dotver exec 8.0 -- rider64 MySolution.sln
$ dotver exec 8.0 -- devenv MySolution.sln
```

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

### Update or remove dotver itself

```bash
$ dotver self-update --check
Installed: 0.4.0  Latest: 0.4.1 (v0.4.1)
An update is available. Run dotver self-update to install it.

$ dotver self-update
Updated 0.4.0 -> 0.4.1
  ~/.local/bin/dotver  (dotver-linux-x64, sha256 43a402c3...)

$ dotver self-update --version 0.4.0     # pin or roll back to an exact release
$ dotver self-update --check --json      # currentVersion, latestVersion, tag, updateAvailable, canSelfUpdate, reason, repository
```

`self-update` downloads the asset for this platform from the GitHub release,
verifies it against the release's `checksums.txt` and atomically replaces the
running binary (on Windows the previous image is parked as `dotver.exe.old`
and removed on the next update). It only applies to the standalone binary;
npm and WinGet installs are updated through their package manager, and
`--check` says so in `reason` when it cannot self-update.
`DOTVER_UPDATE_REPOSITORY=owner/repo` points a fork at its own releases, but
an environment variable alone never decides where the binary comes from:
with a non-default repository, `self-update` refuses until you also pass
`--allow-unofficial-source` and prints the repository it is about to trust.

```bash
$ DOTVER_UPDATE_REPOSITORY=me/dotver-fork dotver self-update
Error: DOTVER_UPDATE_REPOSITORY points updates at 'me/dotver-fork' instead of the official stescobedo92/dotver. Its checksums.txt is only as trustworthy as that repository, so re-run with --allow-unofficial-source if this is intended, or unset the variable.

$ DOTVER_UPDATE_REPOSITORY=me/dotver-fork dotver self-update --check --allow-unofficial-source
Warning: updates come from me/dotver-fork, not from stescobedo92/dotver.
Installed: 0.4.1  Latest: 0.4.2 (v0.4.2)
...
```

Removing dotver is a dry run until you confirm it:

```bash
$ dotver self-uninstall
┌───────────────────────┬────────────────────────────────────────────────────────────────────────┐
│ Action                │ Target                                                                 │
├───────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ remove dotver block   │ ~/.bashrc                                                              │
│ remove dotver block   │ ~/.zshrc                                                               │
│ delete directory      │ ~/.dotver                                                              │
│ binary (manual)       │ Delete the binary yourself: ~/.local/bin/dotver (a running executable  │
│                       │ cannot delete itself).                                                 │
└───────────────────────┴────────────────────────────────────────────────────────────────────────┘
Dry run. Re-run with --yes to apply, or add --keep-sdks to keep the downloaded SDKs.

$ dotver self-uninstall --yes               # removes them all, including ~/.dotver
$ dotver self-uninstall --yes --keep-sdks   # same, but keeps installs/ and runtimes/
```

The uninstall removes the dotver blocks from your shell profiles (through
symlinks, keeping everything else), the shims entry from the Windows user
`PATH` and the `DOTVER_HOME` user variable, and then `~/.dotver`. It only
touches blocks and variables that point at the current `DOTVER_HOME`, so a
custom home never uninstalls another installation. The binary itself cannot
delete its running image, so the last line tells you how to remove it for
the way it was installed (standalone, npm or WinGet).

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
`outdated` and `audit` still report external SDKs so end-of-life or
vulnerable system installs stay visible.

## Automatic configuration

`dotver` automatically configures on first use:

- shims in `~/.dotver/shims`
- persistent user `PATH`
- PowerShell profiles on Windows and supported profiles on Linux/macOS
- `current` and `default` when installing an SDK (opt out per install with
  `--no-use` and/or `--no-default`)

Script-oriented and read-only commands skip this bootstrap so they stay
side-effect free: `exec`, `each`, `shell`, `env`, `which`, `explain`, `scan`,
`audit`, `tools`, `mirror`, `completions`, `self-update` and `self-uninstall`
never rewrite shims, `PATH`, or profiles. The `dotnet` shim itself never
bootstraps either: running `dotnet build` only runs dotnet.

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

### Environment variables

| Variable | Effect |
|---|---|
| `DOTVER_HOME` | Root of dotver's state (default `~/.dotver`) |
| `DOTVER_VERSION` | Exact installed version to select; set by `exec`, `each` and `shell` for their children |
| `DOTVER_SHELL` | Set inside `dotver shell` to the pinned version (for prompts) |
| `DOTVER_NO_BOOTSTRAP` | `1` keeps dotver from touching PATH, profiles or the registry |
| `DOTVER_NO_CACHE` | `1` disables the inventory cache |
| `DOTVER_UPDATE_REPOSITORY` | `owner/repo` for `self-update`; requires `--allow-unofficial-source` |
| `HTTPS_PROXY`, `HTTP_PROXY`, `NO_PROXY` | Honored for every download |

## Documentation used

- `Spectre.Console` and `Spectre.Console.Cli` via Context7 for `CommandApp` structure, DI, prompts, and testing.
- Microsoft Learn for:
  - `global.json` and SDK selection.
  - `dotnet-install` script.
  - SDK installation detection.
  - packaging/publishing on WinGet.
