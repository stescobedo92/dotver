# DotVer — 7 features (mix dev / equipos / CI / adopción)

Approved by user 2026-10-18 (option D). Implementation order: 5, 1, 2, 3, 4, 6, 7.
Each feature ships only after a cybersecurity subagent review approves its diff.
Commits use global git identity only; no Co-Authored-By trailers.

## 5. `--json` + CI flags
- `--json` on `list`, `current`, `list-remote`, `doctor` (+ `outdated` later). Schema versioned (`schemaVersion: 1`), camelCase, written to stdout without Spectre markup.
- `install --no-use` / `--no-default`: stop forcing current+default. Default (both flags absent) keeps current behavior.

## 1. `dotver local [version]` + precedence
- Writes/updates `global.json` in current dir preserving unknown properties; `--roll-forward`, `--clear` removes sdk pin, no-arg shows effective pin.
- Precedence becomes: global.json > current > default > system fallback.
- Validate version tokens strictly (SdkVersion parse or install-plan resolution); never write unvalidated text into global.json. Atomic write (temp + replace).

## 2. `dotver ensure`
- Reads nearest global.json, checks inventory with rollForward rules, installs missing SDK via InstallService. No global.json => exit 1 with clear message. `--use` also sets current. Never changes default.

## 3. `dotver exec <token> -- <cmd...>` + `dotver which [token]`
- Runs child process with DOTNET_ROOT=<HostRoot>, inherited stdio, propagates exit code, does not mutate config. `which` prints resolved dotnet path (or selection-resolved path with no token).

## 4. `dotver outdated` + `dotver upgrade`
- Cross managed inventory vs remote channels: newer patch in same channel, EOL/maintenance/preview flags. `upgrade [token|--all]` installs latest of same channel, repoints current/default/aliases, optional `--prune-old`.

## 6. `dotver completions <shell>` + `use system` / `deactivate`
- Static completion scripts (powershell/bash/zsh/fish) calling back into `dotver list --json` where dynamic. `use system` and `default system` clear persisted selection; `deactivate` alias clears current.

## 7. `dotver doctor --fix` + `dotver prune`
- `--fix`: regenerate shims/PATH (bootstrap), drop current/default/aliases pointing to missing SDKs, drop nonexistent registered roots, report incomplete managed installs.
- `prune`: per-SDK disk usage; `--keep-latest-per-channel` removes older managed patches; dry-run by default, `--yes` executes.

## Security invariants (all features)
- No shell string interpolation: ProcessSpec.ArgumentList only (no UseShellExecute, no `sh -c` with user input).
- Paths from user input: Path.GetFullPath + containment checks before any recursive delete (must stay under ManagedInstalls).
- JSON/file writes: validated tokens, atomic replace, preserve unknown JSON properties via JsonNode.
- Network stays limited to existing official endpoints (dot.net installer, release-metadata). No new endpoints, no credentials, no telemetry.
- Spectre markup: escape user-controlled values with Markup.Escape.
