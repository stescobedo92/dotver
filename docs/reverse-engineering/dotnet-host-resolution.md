# How `dotnet.exe` resolves installs, SDKs and downloads (REA evidence)

This document records what was recovered from the shipped .NET host binaries
with [REA](https://github.com/morluto/rea) (Ghidra 12.1.4 provider) and how
dotver's download module (`dotver download`, `dotver install a b c`,
`HostPlatformResolver`, `DotnetInstallLocations`) mirrors that behaviour.
Every finding states what was **observed** in the binary, what is
**inferred**, and what is only **documented** by Microsoft and could not be
observed on this (Windows x64) machine.

Screenshots of each discovery live in [`screenshots/`](screenshots/); the
runtime corroboration (`COREHOST_TRACE=1`) is in
[`evidence/host-trace.txt`](evidence/host-trace.txt).

## Targets

| Target | Path | SHA-256 | Role |
|---|---|---|---|
| `dotnet.exe` (muxer) | `C:\Program Files\dotnet\dotnet.exe` (167 208 bytes, 2026-10-23) | `1e881ec1786c31ba712d052258b4ad4202ac155df5965794030037878abb822f` | Thin launcher: finds and loads `hostfxr.dll`, prints the "install .NET" message |
| `hostfxr.dll` 10.0.14 | `C:\Program Files\dotnet\host\fxr\10.0.14\hostfxr.dll` | `5b9358572daa76176f97f1bb80fe764b24ee1bc2cce0579230e7f9a28e3fbbed` | The resolver: global install location, `global.json`, SDK roll-forward, RID, download links |

Both binaries reference a PDB that is not available locally (`dotnet.pdb`
GUID `6287e03a-…`, `hostfxr.pdb` GUID `a9047458-…`), so function names below
are Ghidra's `FUN_<address>` names plus the role inferred from strings and
call shape. Pseudocode is decompiler output, not source.

Method: `open_binary` → `list_strings` → `xrefs` on the interesting strings →
`resolve_containing_procedure` → `procedure_pseudo_code` / `read_bytes`,
then one runtime run with `COREHOST_TRACE=1` to confirm the recovered trace
strings are the ones the host actually prints.

## Finding ledger

### dotnet.exe (the muxer)

| # | Finding | Observation (evidence) | Interpretation | dotver |
|---|---|---|---|---|
| D1 | The muxer resolves `hostfxr` by enumerating `<own dir>\host\fxr\*`, parsing every child as a version, keeping the highest, and loading `hostfxr.dll` from it. | `FUN_140003230`: trace strings `Reading fx resolver directory=[%s]`, `Considering fxr version=[%s]...`, `Detected latest fxr version=[%s]...`, `Resolved fxr [%s]...`, error `[%s] does not contain any version-numbered child folders`, `the required library hostfxr.dll could not be found in [%s]` (ev_e20e9bf2…, xrefs ev_b33dbbe6…). Runtime: `host-trace.txt` lines 7–12 show exactly that sequence for 10.0.14 / 6.0.36 / 8.0.33. | The muxer never consults the registry or environment variables for its root: the root is the directory that contains `dotnet.exe`. | `InventoryService` already treats each root as self-contained; nothing changed. |
| D2 | The root search is driven by a flag byte and an embedded placeholder. | `FUN_1400016c0` logs `.NET root search location options: %d` from `DAT_14001b0f0`; `FUN_140001280` reads the embedded app-relative path sentinel `19ff3e9c3602ae8e841925bb461a0adb064a1f1903667a5e0d87e8f608f425ac` (`The app-relative .NET path is not embedded.`, `Embedded app-relative .NET path: '%s'`) (ev_758c865f…, ev_6accb6c7…). | This is the shared `fxr_resolver` code: the apphost patches those bytes at build time; the muxer leaves them untouched and uses its own directory. | Not applicable (dotver always launches a concrete `dotnet` host). |
| D3 | When no runtime can be found, the host builds `https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=<arch>&rid=<rid>&os=<os>` and appends `&apphost_version=<ver>`. | `FUN_140007520` concatenates `https://aka.ms/dotnet-core-applaunch?`, `missing_runtime=true`, `&arch=` + `DAT_140016ef0`, `&rid=` + (`DOTNET_RUNTIME_ID` or `win-x64`), `&os=` + OS moniker (ev_8e9fe517…). `read_bytes 0x140016ef0` = `x64\0win-x64\0` (ev_3bd3e1bd…). `FUN_140002100` prints `You must install or update .NET to run this application. … Download the .NET runtime: %s&apphost_version=%s` (ev_05fba60f…). | The RID is compiled in per build (`win-x64` here) and can only be overridden by `DOTNET_RUNTIME_ID`; the architecture string is a separate compiled-in constant. | `DotnetHostLinks.MissingRuntimeUrl` reproduces the exact query string; `HostPlatformResolver` reproduces the RID. |
| D4 | The `os=` value is `win7`/`win8`/`win81` for NT 6.x and `win` + major for NT ≥ 10 (`win10`, `win11`), with `win10` as the fallback. | `FUN_140004e20` calls `ntdll!RtlGetVersion`, maps 6.1/6.2/6.3, otherwise concatenates `DAT_140016db8` (= `win`, ev_2601b8e5…) with the major version (ev_385a0563…). | Marketing name, not build number; the URL only needs a coarse OS family. | Documented; dotver does not need the moniker. |
| D5 | Host settings are read as `DOTNET_HOST_<NAME>` first, then `COREHOST_<NAME>` (legacy). | `FUN_140007da0` formats `DOTNET_HOST_%s` with `TRACE`, falls back to `COREHOST_%s`, and prints `Tracing enabled @ %s` (ev_5e92565f…). The runtime run with `COREHOST_TRACE=1` produced that line. | Both spellings keep working; `DOTNET_HOST_*` wins. | Documented for users of `dotver doctor`. |
| D6 | The muxer contains **no** `DOTNET_ROOT`, `InstalledVersions` or `Program Files` strings. | `search_strings` for those literals returned nothing in the defined-string inventory (ev_569ec2cd…, ev_96482850…, ev_60930918…). | Global-location logic is not in the muxer; it lives in `hostfxr.dll` (and the apphost). An empty search is not proof of absence, but it is consistent with D1. | Confirms that `dotver which`/`exec` must pass an explicit host path. |

### hostfxr.dll 10.0.14 (the resolver)

| # | Finding | Observation (evidence) | Interpretation | dotver |
|---|---|---|---|---|
| H1 | On Windows the **registered** global location is the registry value `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation`, opened through the 32-bit registry view. | `FUN_18002e9b0` builds `SOFTWARE\dotnet` + `\Setup\InstalledVersions\` + arch name, value `InstallLocation`, root `HKEY_LOCAL_MACHINE` (`0x80000002`; `HKEY_CURRENT_USER` only under the `_DOTNET_TEST_REGISTRY_PATH` test hook) (ev_b953133c…). `FUN_18002f330` calls `RegOpenKeyExW(…, 0x20219, …)` = `KEY_READ | KEY_WOW64_32KEY`, then `RegGetValueW(REG_SZ)`, logging `Found registered install location '%s'.` (ev_06487dcc…). | Because of `KEY_WOW64_32KEY`, on 64-bit Windows the value physically lives under `WOW6432Node`; this machine has `HKLM\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\InstallLocation = C:\Program Files\dotnet\`. | `DotnetInstallLocations.WindowsRegistryKey` + `SystemEnvironmentContext.ReadRegistryInstallLocation` (`RegistryView.Registry32`). |
| H2 | The **default** Windows location is `%ProgramFiles%\dotnet` (x64/arm64) or `%ProgramFiles(x86)%\dotnet` (x86); an x64 install on an arm64 host is `%ProgramFiles%\dotnet\x64`. | `FUN_18002e840`: arch index 7 → `ProgramFiles`, 8 → `ProgramFiles(x86)`, appends `dotnet`, and appends `DAT_18004f248` (= `x64`, ev_11cd4591…) when `FUN_180031120` (emulation check) is true; `_DOTNET_TEST_DEFAULT_INSTALL_PATH` overrides it in test builds (ev_e910f627…). | Arch indices 7/8 are inferred from the strings they select. | `DotnetInstallLocations.WindowsDefaultInstallRoot`. |
| H3 | Order: registered location first, default second, duplicates removed case-insensitively. | `FUN_18002f210` tries the test hook, else `FUN_18002f330(7, …)` (ev_e237526b…). `FUN_18002f6e0` pushes the registered path, then the default path unless `_wcsicmp` says it is the same (ev_61845ecb…). `hostfxr_get_dotnet_environment_info` logs `Using global install location [%s].` / `No default dotnet installation could be obtained.` (ev_c6be78ab…). | Same order Microsoft documents for the apphost. | `DotnetInstallLocations.Resolve` (same order, same dedupe); `GetKnownDotnetRoots` now starts with it. |
| H4 | `DOTNET_MULTILEVEL_LOOKUP` is still parsed, but only when the caller does not disable multi-level lookup; `1` enables it, anything else disables it. | `FUN_180032450`: if the caller flag is 0, defaults to enabled, reads `DOTNET_MULTILEVEL_LOOKUP`, sets enabled = (`_wtoi` == 1), logs `Multilevel lookup is %s`; when enabled it appends every global location that differs from `dotnet_root` (ev_ddbb9d1e…). | Matches the .NET 7 breaking change: the host code keeps the capability, callers for .NET 7+ apps pass the "disabled" flag. The runtime trace did not print the multilevel line for `--list-sdks`. | Documented; dotver does not rely on multi-level lookup. |
| H5 | SDK resolution: `Resolving SDKs with version = '%s', rollForward = '%s', allowPrerelease = %s`, over the locations from `global.json` `sdk/paths` (with the `$host$` sentinel meaning "the dotnet root") or just `dotnet_root`, each suffixed with `sdk`. | `FUN_180022c90` (ev_587f6c50…) iterates `FUN_1800232f0` (ev_f3c126be…), logs `SDK path [%s] does not exist` / `SDK path resolved to [%s]`, and on failure prints the `A compatible .NET SDK was not found … Download a .NET SDK: https://aka.ms/dotnet/download` block. Policies: `latestPatch`, `latestFeature`, `latestMinor`, `latestMajor` strings at 0x18004a8a0…. | The roll-forward vocabulary and `allowPrerelease` default are the ones dotver's `GlobalJsonMatcher` already implements. | Unchanged; `DotnetHostLinks.SdkDownload` / `SdkNotFound` carry the links. |
| H6 | Inside one location: with an exact version and a `disable`/`patch` policy the host probes `<loc>\sdk\<version>\dotnet.dll` first (`Found requested SDK [%s]`); otherwise it enumerates directories and filters with the policy (`Ignoring version [%s] because it does not match the roll-forward policy`, `Ignoring version [%s] without dotnet.dll`). | `FUN_180025240` (ev_7beca394…). Runtime: `host-trace.txt` line 38 `Ignoring version [10.0.301] without dotnet.dll` — a half-removed SDK folder on this machine is skipped, which is why `dotnet --list-sdks` shows only 8.0.427 and 10.0.403. | A directory under `sdk/` only counts when it holds `dotnet.dll`. | `InventoryService` already requires `sdk/<version>`; worth keeping the `dotnet.dll` check in mind for `doctor`. |
| H7 | `global.json` is searched from the working directory upward until the root. | `FUN_180023ec0`: `Probing path [%s] for global.json`, `Found global.json [%s]`, `Terminating global.json search at [%s]` (ev_25dec2cd…). Runtime: lines 25–36 of the trace walk from the scratch dir up to `C:\`. | Same walk dotver's `GlobalJsonService` performs. | Unchanged. |
| H8 | The RID the host reports is `DOTNET_RUNTIME_ID` when set, else the compiled-in `win-x64`; the architecture string is the compiled-in `x64`. | `FUN_180032350` (ev_b175b175…); `read_bytes 0x18004f248` = `x64\0x86\0win-…` (ev_11cd4591…); `dotnet --info` prints `RID: win-x64`, `Architecture: x64`. | The RID is a build property, which is why the release metadata keys archives by RID. | `HostPlatformResolver.Resolve`/`FromRid`; `dotver download --rid`. |
| H9 | hostfxr's version of the download link adds `framework=<name>&framework_version=<ver>` when a specific framework is missing and `missing_runtime=true` otherwise, then `&arch=x64&rid=…&os=…`. | `FUN_180033580` (ev_b2499668…); message template `To install missing framework, download:` at 0x18004a110. | Same builder as D3, parameterised. | `DotnetHostLinks.MissingRuntimeUrl(framework, version, …)`. |
| H10 | Other host switches present as strings: `DOTNET_ROLL_FORWARD`, `DOTNET_ROLL_FORWARD_TO_PRERELEASE`, `DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX`, `DOTNET_DISABLE_RUNTIME_VERSIONS`, `DOTNET_ADDITIONAL_DEPS`, `DOTNET_` prefix with the `COMPlus_*` deprecation warning, and the `_DOTNET_TEST_*` hooks. | String inventory ev_940e21c9…. | Inventory only; not traced to code in this pass. | Listed for completeness. |
| H11 | `hostfxr.dll` does **not** read `DOTNET_ROOT`: it receives `dotnet_root` from its caller (`Using dotnet root path [%s]`, `dotnet_root=%s`). | `search_strings "ROOT"` only hits parameter/trace strings (ev_d5d965b3…). | `DOTNET_ROOT`/`DOTNET_ROOT_X64`/`DOTNET_ROOT_X86` are honoured by the apphost before hostfxr is loaded (documented by Microsoft; the apphost was not analysed here). | Unchanged: dotver sets `DOTNET_ROOT` for children exactly as before. |

### Release metadata and install scripts (shipped scripts, not decompiled)

| # | Finding | Observation | dotver |
|---|---|---|---|
| M1 | `releases-index.json` → per-channel `releases.json`; each release lists `sdks[].files[]` with `name`, `rid`, `url`, `hash` (SHA-512 hex). Archives are `.zip` for `win-*` and `.tar.gz` for every other RID; `.exe`/`.pkg` installers share the same RID. | `evidence/releases-8.0-files.txt` (fetched 2026-11-06): 8.0.427 publishes `linux-arm`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm`, `linux-musl-arm64`, `linux-x64`, `osx-x64`, `osx-arm64`, `win-arm64`, `win-x64`, `win-x86`. | `SdkDownloadService` selects `rid == <rid> && name ends with <ext>` and verifies the SHA-512 before keeping the file. |
| M2 | `dotnet-install.sh` derives the OS token from `uname` (`Darwin`→`osx`, `FreeBSD`→`freebsd`, `Linux`→`linux` or `linux-musl` when `ldd --version` mentions musl) and the architecture from `uname -m` (`x64`, `arm`, `arm64`, `s390x`, `ppc64le`, `loongarch64`, `riscv64`); `dotnet-install.ps1` accepts `amd64|x64`, `x86`, `arm`, `arm64` and uses `.zip` (tar.gz only from 11.0 preview 3). Download link: `<feed>/Sdk/<version>/dotnet-sdk-<version>-<os>-<arch>.<ext>`. | Script lines 187–220, 295–342, 733–749 (sh); 259–272, 345–372, 647 (ps1). | `HostPlatformResolver` (OS aliases, architecture aliases, musl loader probe, archive extension). |

## Per-OS summary (what the host does, and what is observed vs documented)

| OS | Registered global location | Default global location | RID | Status |
|---|---|---|---|---|
| Windows | `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (32-bit view → `WOW6432Node` on x64) | `%ProgramFiles%\dotnet`, `%ProgramFiles(x86)%\dotnet` for x86, `%ProgramFiles%\dotnet\x64` for x64-on-arm64 | compiled-in `win-<arch>`, `DOTNET_RUNTIME_ID` overrides | **Observed** in hostfxr 10.0.14 (H1–H3, H8) and at runtime |
| Linux (glibc) | `/etc/dotnet/install_location_<arch>`, then `/etc/dotnet/install_location` (one path per file) | `/usr/share/dotnet` | `linux-<arch>` | **Documented** (Microsoft Learn, distribution packaging); not observable from a Windows binary |
| Linux (musl / Alpine) | same files | `/usr/share/dotnet` | `linux-musl-<arch>`; the install script detects musl via `ldd --version` | Documented + `dotnet-install.sh` (M2) |
| macOS | same files | `/usr/local/share/dotnet` | `osx-<arch>` | Documented + `dotnet-install.sh` (M2) |
| FreeBSD | same files | `/usr/share/dotnet` | `freebsd-<arch>` | `dotnet-install.sh` (M2) |
| All | `DOTNET_ROOT` (and `DOTNET_ROOT_X64`/`_X86`) are read by the apphost, not by hostfxr | | | Documented; H11 shows hostfxr receives the root from its caller |

`DotnetInstallLocations.Resolve` implements exactly that table (registered first,
default second, case-insensitive dedupe on Windows), and
`SystemEnvironmentContext.GetKnownDotnetRoots` feeds it to the inventory ahead
of the user-local roots the install scripts use (`%LocalAppData%\Microsoft\dotnet`,
`~/.dotnet`).

## What dotver now does with this

- `dotver download <tokens…> [--rid …]… [--out DIR] [--dry-run] [--json]`
  resolves each token like `dotver install` (exact version, channel, `lts`,
  `sts`, `latest`), reads the channel's `releases.json`, picks the archive
  whose `rid` matches each requested RID (default: the RID of this machine,
  derived like the host does), streams it to `<file>.part`, verifies the
  published SHA-512, renames it, and writes a `<file>.sha512` sidecar in
  `sha512sum -c` layout. It prints the ready-to-run
  `dotver install <version> --from <file> --sha512 <hex>` line for every
  archive, so air-gapped machines can install with the existing offline path.
- `dotver install 8.0 9.0 lts` installs several SDKs in order; a failure is
  reported per token without stopping the others, duplicates are skipped,
  and the last host-architecture SDK becomes current/default (unless
  `--no-use`/`--no-default`).
- `HostPlatformResolver` and `DotnetHostLinks` expose the RID/arch/OS logic
  and the exact `aka.ms/dotnet-core-applaunch` query string.
- `DotnetInstallLocations` + `SystemEnvironmentContext` discover the
  machine-wide root the way the host does (registry / `/etc/dotnet`), then the
  default per OS.

End-to-end check on this machine (2026-11-06):

```text
$ dotver download 8.0.427 --rid linux-musl-arm64 --out ./archives --json
  status: downloaded, bytes: 212137894,
  sha512: fb9d8c95b384b7d9…6439951a0 (published hash)
$ sha512sum -c dotnet-sdk-8.0.427-linux-musl-arm64.tar.gz.sha512
dotnet-sdk-8.0.427-linux-musl-arm64.tar.gz: OK
```

## Residual unknowns

- The apphost (`apphost.exe` template) was not opened: the `DOTNET_ROOT`
  handling and the app-relative placeholder patching are documented, not
  observed.
- Linux/macOS `hostfxr` builds were not available on this machine; their
  `/etc/dotnet/install_location` logic is taken from Microsoft's
  documentation and the install script.
- Architecture index values (7 = x64, 8 = x86) in `FUN_18002e840` are
  inferred from the strings they select, not from a symbol table.
- The REA session ledger is ephemeral: the evidence IDs above identify the
  records produced during the analysis session on 2026-11-06; re-running the
  same operations on the same bytes reproduces the same observations.

## Screenshots

| File | Shows |
|---|---|
| `screenshots/01-dotnet-exe-strings.png` | Muxer string inventory: `host/fxr` resolution, `aka.ms` link, `DOTNET_RUNTIME_ID`, `win-x64`, `DOTNET_HOST_%s`/`COREHOST_%s` |
| `screenshots/02-dotnet-exe-fxr-resolution.png` | `FUN_140003230` pseudocode next to the runtime trace that prints the same strings |
| `screenshots/03-dotnet-exe-applaunch-url.png` | `FUN_140007520` URL builder, `FUN_140004e20` OS moniker, bytes at `0x140016ef0` |
| `screenshots/04-hostfxr-strings.png` | hostfxr strings: registry key, `InstallLocation`, `ProgramFiles(x86)`, `DOTNET_MULTILEVEL_LOOKUP`, roll-forward vocabulary |
| `screenshots/05-hostfxr-install-location.png` | Registry key builder, `RegOpenKeyExW(KEY_WOW64_32KEY)`, default Program Files path, registered-then-default order |
| `screenshots/06-hostfxr-multilevel-lookup.png` | `DOTNET_MULTILEVEL_LOOKUP` parsing gated by the caller |
| `screenshots/07-hostfxr-sdk-resolution.png` | SDK resolution, `$host$`, `global.json` walk, `dotnet.dll` probe, plus the runtime trace |
| `screenshots/08-release-metadata-rids.png` | `releases.json` files keyed by RID and the install-script URL/OS/arch rules |
| `screenshots/09-dotver-download-dry-run.png` | `dotver download 8.0 9.0 lts --rid …×4 --dry-run` against the live metadata |
| `screenshots/10-dotver-download-verified.png` | Real download of `linux-musl-arm64`, `sha512sum -c … OK`, and the test run (274 passed) |
