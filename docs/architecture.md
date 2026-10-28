# Architecture

## Layers

- `DotVer.Domain`
  - immutable models (`SdkInstallation`, `SdkVersion`, `DotVerConfig`)
  - comparison and representation rules.
- `DotVer.Application`
  - use cases and coordination.
  - inventory, resolution, selection, interception, shims, and installation services.
- `DotVer.Infrastructure`
  - operating system, processes, HTTP, JSON persistence, and DI registration.
- `DotVer.Cli`
  - `Spectre.Console` / `Spectre.Console.Cli`.
  - user commands and TUI dashboard.
- `DotVer.Tests`
  - unit tests with filesystem sandboxes and `IConfigStore` / `IProcessRunner` test doubles.

## Key Flows

### Active SDK Resolution

1. If `DOTVER_VERSION` is set, it must name an exact installed version
   (no aliases or prefixes); otherwise resolution fails loudly. `dotver exec`
   sets it for its child process.
2. Search for `global.json` from the current directory upward and resolve it
   with its `rollForward` against the local inventory (`GlobalJsonMatcher`;
   roll-forward never selects a version below the pin). A satisfied project
   pin wins over the persisted global selection. `dotver local` writes pins,
   `dotver ensure` installs a missing pinned SDK.
3. If not applicable, use the persisted `currentVersion` (`dotver use`;
   `use system`/`deactivate` clear it).
4. If not applicable, use `defaultVersion`.
5. If not applicable, fall back to the official system host.

### Inventory

Sources:

- `~/.dotver/installs/*`
- well-known official roots per platform
- `DOTNET_ROOT`
- `PATH` entries
- manually registered roots

Each root is classified and then projected into one or more `SdkInstallation` instances.

### Interception

The shims call:

```text
dotver intercept -- <original-args>
```

Rules:

- `dotnet --version` -> returns the resolved active version.
- `dotnet --list-sdks` -> returns the consolidated inventory.
- `dotnet --info` -> delegates to the real host and appends dotver diagnostics.
- any other command -> is forwarded to the resolved `dotnet`.

The forwarded host is always an absolute path: the selected SDK, a known
system root, or a `dotnet` found on `PATH` outside the current directory and
the shims directory. With no host available the shim exits non-zero with a
clear message instead of letting process creation search the cwd. The shim
path never runs the environment bootstrap.

### Automatic Environment Bootstrap

On every useful command (never on `intercept`, `exec`, `which`, `completions`
and the other script-oriented commands), `dotver` ensures:

- idempotent shim generation
- presence of `~/.dotver/shims` in `PATH`
- persistence of `DOTVER_HOME`
- best-effort update of PowerShell profiles on Windows
- best-effort update of shell profiles on Unix (fish only when `~/.config/fish` exists)

`DOTVER_NO_BOOTSTRAP=1` disables everything except the shim files under
`~/.dotver`. Profiles are written atomically through symlinks, the set of
configured profiles is remembered in `~/.dotver/bootstrap.json` so a block the
user deletes is not re-added, and the Windows user `PATH` is read and written
unexpanded so its `REG_EXPAND_SZ` kind survives.

The parent terminal session cannot be modified directly from a child process on Windows; therefore the persistent change is fully reflected only when opening a new terminal.

## Design Decisions

- Persistent state lives in `~/.dotver/config.json`.
- Managed installations use the official `dotnet-install` script to preserve cross-platform compatibility and avoid elevated privileges.
- Shims are simple files (`.cmd`, `.ps1`, `bash/fish`) so that behavior is visible and debuggable.

## Security Decisions

- SDK version strings are strict SemVer (suffix identifiers of
  `[0-9A-Za-z-]`, dot separated, length capped); they are used in file paths
  and process arguments, so separators and traversal never parse. Remote
  release metadata is re-validated the same way, and managed install roots
  must stay under `~/.dotver/installs`.
- Recursive deletes (uninstall/prune/upgrade) only run on the exact managed
  layout `<installs>/<version>`, reject symlinks/junctions, and prune
  re-validates current/default/alias/`global.json` protections immediately
  before each delete.
- `global.json` and config writes are atomic (temp file with `CreateNew` +
  rename); malformed or oversized (>1 MiB) `global.json` files are ignored on
  reads and never overwritten on writes. Reads *and* the read-modify-write of
  `dotver local` open the file without following links (`O_NOFOLLOW` /
  `FILE_FLAG_OPEN_REPARSE_POINT`), so a symlinked `global.json` in a cloned
  repository can never get its target copied into the repo.
- Child processes never go through a shell (`ArgumentList`), and
  `ProcessRunner` only accepts absolute file names: executables such as
  `pwsh` and the `dotnet` host behind the shim are resolved by
  `ExecutableLocator`, which skips the current directory and the shims
  directory, so a cloned repository cannot plant a fake tool. When no host
  can be found the shim fails loudly instead of spawning a bare `dotnet`.
- Inventory only accepts absolute root hints; relative `PATH`/`DOTNET_ROOT`
  entries are ignored and `register-root` stores the normalized path of an
  existing directory.
- The official `dotnet-install` script and the releases index are fetched by
  a narrow HTTP client: https only, an allow-list of Microsoft hosts checked
  on every redirect hop, a redirect cap and a size cap. On Windows the script
  is executed only after `Get-AuthenticodeSignature` reports a valid signature
  issued to Microsoft Corporation (TLS authenticates the CDN, Authenticode the
  publisher). No hash is pinned because Microsoft rotates the script.
- The npm package embeds the SHA-256 of each release binary (taken from the
  build artifacts of the same workflow run) and `postinstall` verifies the
  download before it becomes the machine's `dotnet`.
- Bootstrap is opt-out (`DOTVER_NO_BOOTSTRAP=1`), never runs on the shim
  path, writes profiles atomically through symlinks, remembers configured
  profiles so a deleted block is not re-added, and keeps the Windows user
  `PATH` as unexpanded `REG_EXPAND_SZ`.
- Release workflows run with `contents: read` by default, grant `write` only
  to the jobs that publish, pin every action to a commit SHA, expose tokens
  only to the step that uses them, and derive WinGet/npm checksums from the
  build artifacts rather than from re-downloaded assets.
- Shell completions consume `dotver __complete`, whose output is filtered to
  shell-inert charsets at the boundary; the bash script reads candidates
  line-by-line instead of `compgen -W` re-expansion.
- `doctor --fix` never destroys config from negative evidence: with missing
  registered roots or an empty inventory it skips the config repair, never
  auto-removes registered roots, and writes a `config.json.bak` backup before
  saving repairs.
