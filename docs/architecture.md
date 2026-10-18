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

### Automatic Environment Bootstrap

On every useful command, `dotver` ensures:

- idempotent shim generation
- presence of `~/.dotver/shims` in `PATH`
- persistence of `DOTVER_HOME`
- best-effort update of PowerShell profiles on Windows
- best-effort update of shell profiles on Unix

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
  reads and never overwritten on writes.
- Child processes never go through a shell (`ArgumentList`), and executables
  such as `pwsh` are resolved to absolute PATH entries that exclude the
  current directory, so a cloned repository cannot plant a fake tool.
- The official `dotnet-install` script is fetched over HTTPS from
  `dot.net` on each install and executed trusting TLS (the same model as
  `setup-dotnet`); no hash is pinned because Microsoft rotates the script.
- Shell completions consume `dotver __complete`, whose output is filtered to
  shell-inert charsets at the boundary; the bash script reads candidates
  line-by-line instead of `compgen -W` re-expansion.
- `doctor --fix` never destroys config from negative evidence: with missing
  registered roots or an empty inventory it skips the config repair, never
  auto-removes registered roots, and writes a `config.json.bak` backup before
  saving repairs.
