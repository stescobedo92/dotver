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

1. Search for `global.json` from the current directory upward.
2. If `currentVersion` exists, use it first.
3. If not applicable, use `defaultVersion`.
4. If not applicable, resolve `global.json` and its `rollForward` against the local inventory.
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
