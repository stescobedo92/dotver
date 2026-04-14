# Clean Architecture Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split every monolithic source file into one-class/interface/record-per-file, add service interfaces to the Application layer (DIP), fix DashboardCommand tight coupling, and reorganize tests.

**Architecture:** The Clean Architecture layers (Domain → Application → Infrastructure → Cli) already exist. The only structural change is splitting large files and adding interfaces. No logic changes — all tests must pass identically after the refactor.

**Tech Stack:** .NET 10, C#, Spectre.Console, xUnit, Microsoft.Extensions.DependencyInjection

---

## Execution Waves (for parallel subagent dispatch)

- **Wave 1 (parallel):** Task 1, Task 2, Task 3 — independent file creations
- **Wave 2:** Task 4 — Application services (needs interfaces from Task 3)
- **Wave 3 (parallel):** Task 5, Task 6 — Infrastructure and CLI (need Task 4 interfaces)
- **Wave 4:** Task 7 — Tests reorganization
- **Wave 5:** Task 8 — Delete monolithic files + verify build + verify tests

---

## Task 1: Split Domain/Models.cs

**Files to create:**
- `src/DotVer.Domain/Enums/SdkOriginKind.cs`
- `src/DotVer.Domain/Enums/ShellKind.cs`
- `src/DotVer.Domain/Enums/SdkRollForward.cs`
- `src/DotVer.Domain/SdkVersion.cs`
- `src/DotVer.Domain/SdkInstallation.cs`
- `src/DotVer.Domain/GlobalJsonRequest.cs`
- `src/DotVer.Domain/SdkSelection.cs`
- `src/DotVer.Domain/DotVerConfig.cs`
- `src/DotVer.Domain/ProcessSpec.cs`
- `src/DotVer.Domain/ProcessResult.cs`
- `src/DotVer.Domain/ShimInstallResult.cs`
- `src/DotVer.Domain/ReleaseChannel.cs`
- `src/DotVer.Domain/EnvironmentBootstrapResult.cs`
- `src/DotVer.Domain/InstallPlan.cs`
- `src/DotVer.Domain/InstallOutcome.cs`
- `src/DotVer.Domain/UninstallOutcome.cs`

**Source:** `src/DotVer.Domain/Models.cs` — read this file first, then extract each type.

- [ ] **Step 1: Create `Enums/SdkOriginKind.cs`**

```csharp
namespace DotVer.Domain;

public enum SdkOriginKind
{
    ManagedInstall,
    OfficialInstallRoot,
    CustomPath,
    RegisteredRoot,
    PathProbe,
    Unknown
}
```

- [ ] **Step 2: Create `Enums/ShellKind.cs`**

```csharp
namespace DotVer.Domain;

public enum ShellKind
{
    PowerShell,
    Bash,
    Zsh,
    Fish,
    Cmd
}
```

- [ ] **Step 3: Create `Enums/SdkRollForward.cs`**

```csharp
namespace DotVer.Domain;

public enum SdkRollForward
{
    Default,
    Disable,
    Patch,
    Feature,
    Minor,
    Major,
    LatestPatch,
    LatestFeature,
    LatestMinor,
    LatestMajor
}
```

- [ ] **Step 4: Create `SdkVersion.cs`** — extract the full `SdkVersion` sealed record (including `Pattern`, `CompareTo`, `ToString`, `TryParse`, `Parse`) from `Models.cs`. Add `using System.Text.RegularExpressions;` at the top.

- [ ] **Step 5: Create remaining domain type files** — extract each of the following sealed records verbatim from `Models.cs` into their own file (same namespace `DotVer.Domain;`, no extra usings needed):
  - `SdkInstallation.cs` — `SdkInstallation` record
  - `GlobalJsonRequest.cs` — `GlobalJsonRequest` record
  - `SdkSelection.cs` — `SdkSelection` record
  - `DotVerConfig.cs` — `DotVerConfig` record (includes the `Empty` static property)
  - `ProcessSpec.cs` — `ProcessSpec` record
  - `ProcessResult.cs` — `ProcessResult` record
  - `ShimInstallResult.cs` — `ShimInstallResult` record
  - `ReleaseChannel.cs` — `ReleaseChannel` record
  - `EnvironmentBootstrapResult.cs` — `EnvironmentBootstrapResult` record
  - `InstallPlan.cs` — `InstallPlan` record
  - `InstallOutcome.cs` — `InstallOutcome` record
  - `UninstallOutcome.cs` — `UninstallOutcome` record

- [ ] **Step 6: Verify the Domain project builds** (do NOT delete `Models.cs` yet — that is Task 8)

```bash
cd src/DotVer.Domain && dotnet build
```

Expected: build succeeds (types exist in both old and new files — duplicates will error; if so, the split is wrong. Do not delete Models.cs until Task 8).

> **Note:** Since `Models.cs` still exists, the compiler will reject duplicate type definitions. The goal of this step is to confirm the new files are syntactically valid by checking for parse errors only. Alternatively, temporarily rename `Models.cs` to `Models.cs.bak` for the build check, then restore it. Task 8 does the final deletion.

---

## Task 2: Split Application/Abstractions.cs

**Files to create:**
- `src/DotVer.Application/Abstractions/IConfigStore.cs`
- `src/DotVer.Application/Abstractions/IEnvironmentContext.cs`
- `src/DotVer.Application/Abstractions/IProcessRunner.cs`
- `src/DotVer.Application/Abstractions/IHttpResourceClient.cs`
- `src/DotVer.Application/Abstractions/DotVerPaths.cs`

**Source:** `src/DotVer.Application/Abstractions.cs` — read this file first, then extract each type.

- [ ] **Step 1: Create `Abstractions/IConfigStore.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IConfigStore
{
    Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Create `Abstractions/IEnvironmentContext.cs`**

```csharp
namespace DotVer.Application;

public interface IEnvironmentContext
{
    string HomeDirectory { get; }
    string DocumentsDirectory { get; }
    string CurrentDirectory { get; }
    string ProcessPath { get; }
    string EntryAssemblyPath { get; }
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
    IReadOnlyList<string> GetPathEntries();
    string? GetEnvironmentVariable(string name);
    string? GetUserEnvironmentVariable(string name);
    void SetUserEnvironmentVariable(string name, string value);
}
```

- [ ] **Step 3: Create `Abstractions/IProcessRunner.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create `Abstractions/IHttpResourceClient.cs`**

```csharp
namespace DotVer.Application;

public interface IHttpResourceClient
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken);
    Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create `Abstractions/DotVerPaths.cs`**

```csharp
namespace DotVer.Application;

public sealed record DotVerPaths(
    string Home,
    string ConfigFile,
    string ManagedInstalls,
    string Shims,
    string Cache,
    string Scripts,
    string Env)
{
    public static DotVerPaths From(IEnvironmentContext environment)
    {
        var home = environment.GetEnvironmentVariable("DOTVER_HOME");
        var root = string.IsNullOrWhiteSpace(home)
            ? Path.Combine(environment.HomeDirectory, ".dotver")
            : home;

        return new DotVerPaths(
            root,
            Path.Combine(root, "config.json"),
            Path.Combine(root, "installs"),
            Path.Combine(root, "shims"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "cache", "scripts"),
            Path.Combine(root, "env"));
    }
}
```

- [ ] **Step 6: Verify syntax** — do NOT delete `Abstractions.cs` yet (Task 8 does that).

---

## Task 3: Create Application Service Interfaces

**Files to create (all new — nothing to delete):**
- `src/DotVer.Application/Services/Interfaces/IGlobalJsonService.cs`
- `src/DotVer.Application/Services/Interfaces/IVersionResolver.cs`
- `src/DotVer.Application/Services/Interfaces/IInventoryService.cs`
- `src/DotVer.Application/Services/Interfaces/ISelectionService.cs`
- `src/DotVer.Application/Services/Interfaces/IInterceptorService.cs`
- `src/DotVer.Application/Services/Interfaces/IShimService.cs`
- `src/DotVer.Application/Services/Interfaces/IEnvironmentBootstrapService.cs`
- `src/DotVer.Application/Services/Interfaces/IInstallService.cs`
- `src/DotVer.Application/Services/Interfaces/IStateService.cs`

- [ ] **Step 1: Create `IGlobalJsonService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IGlobalJsonService
{
    GlobalJsonRequest? TryFind(string workingDirectory);
}
```

- [ ] **Step 2: Create `IVersionResolver.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IVersionResolver
{
    string ResolveToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations);
    SdkInstallation? FindByToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations);
}
```

- [ ] **Step 3: Create `IInventoryService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IInventoryService
{
    Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken);
    string? GetSystemDotnetPath();
}
```

- [ ] **Step 4: Create `ISelectionService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface ISelectionService
{
    Task<SdkSelection> ResolveAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create `IInterceptorService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IInterceptorService
{
    Task<ProcessResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Create `IShimService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IShimService
{
    Task<ShimInstallResult> InstallAsync(CancellationToken cancellationToken);
    string RenderEnvSnippet(ShellKind shellKind);
}
```

- [ ] **Step 7: Create `IEnvironmentBootstrapService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IEnvironmentBootstrapService
{
    Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 8: Create `IInstallService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IInstallService
{
    Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken);
    Task<InstallOutcome> InstallAsync(string versionOrChannel, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 9: Create `IStateService.cs`**

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public interface IStateService
{
    Task<DotVerConfig> GetConfigAsync(CancellationToken cancellationToken);
    Task<SdkInstallation> SetCurrentAsync(string token, CancellationToken cancellationToken);
    Task<SdkInstallation> SetCurrentAndDefaultAsync(string token, CancellationToken cancellationToken);
    Task<SdkInstallation> SetDefaultAsync(string token, CancellationToken cancellationToken);
    Task AddAliasAsync(string alias, string versionToken, CancellationToken cancellationToken);
    Task RemoveAliasAsync(string alias, CancellationToken cancellationToken);
    Task RegisterRootAsync(string path, CancellationToken cancellationToken);
    Task<UninstallOutcome> UninstallManagedAsync(string token, CancellationToken cancellationToken);
}
```

- [ ] **Step 10: Verify Application project builds**

```bash
cd src/DotVer.Application && dotnet build
```

Expected: build succeeds (interfaces are additive — nothing breaks yet).

---

## Task 4: Split Application/Services.cs — implement interfaces

**Files to create:**
- `src/DotVer.Application/Services/GlobalJsonService.cs`
- `src/DotVer.Application/Services/VersionResolver.cs`
- `src/DotVer.Application/Services/InventoryService.cs`
- `src/DotVer.Application/Services/SelectionService.cs`
- `src/DotVer.Application/Services/InterceptorService.cs`
- `src/DotVer.Application/Services/ShimService.cs`
- `src/DotVer.Application/Services/EnvironmentBootstrapService.cs`
- `src/DotVer.Application/Services/InstallService.cs`
- `src/DotVer.Application/Services/StateService.cs`

**Source:** `src/DotVer.Application/Services.cs` — read this file in its entirety first.

For each service:
1. Copy the class verbatim from `Services.cs` into its own file.
2. Add `: IServiceName` to the class declaration.
3. Update constructor parameter types from concrete classes to their interfaces where applicable (see table below).
4. Keep all method bodies identical.

**Constructor parameter changes:**

| Service | Parameter | Old type | New type |
|---|---|---|---|
| `InventoryService` | — | — | — (depends on IConfigStore, IEnvironmentContext — already interfaces) |
| `SelectionService` | `inventoryService` | `InventoryService` | `IInventoryService` |
| `SelectionService` | `configStore` | `IConfigStore` | `IConfigStore` (no change) |
| `SelectionService` | `globalJsonService` | `GlobalJsonService` | `IGlobalJsonService` |
| `SelectionService` | `versionResolver` | `VersionResolver` | `IVersionResolver` |
| `SelectionService` | `environment` | `IEnvironmentContext` | `IEnvironmentContext` (no change) |
| `InterceptorService` | `selectionService` | `SelectionService` | `ISelectionService` |
| `InterceptorService` | `inventoryService` | `InventoryService` | `IInventoryService` |
| `InterceptorService` | `processRunner` | `IProcessRunner` | `IProcessRunner` (no change) |
| `EnvironmentBootstrapService` | `shimService` | `ShimService` | `IShimService` |
| `InstallService` | `inventoryService` | `InventoryService` | `IInventoryService` |
| `StateService` | `versionResolver` | `VersionResolver` | `IVersionResolver` |
| `StateService` | `inventoryService` | `InventoryService` | `IInventoryService` |

**Private fields also change type** to the interface type (e.g., `private readonly InventoryService _inventoryService;` → `private readonly IInventoryService _inventoryService;`).

- [ ] **Step 1: Create `Services/GlobalJsonService.cs`** — copy `GlobalJsonService` from `Services.cs`, add `: IGlobalJsonService`.

```csharp
using System.Text.Json;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class GlobalJsonService : IGlobalJsonService
{
    // body verbatim from Services.cs
}
```

- [ ] **Step 2: Create `Services/VersionResolver.cs`** — copy `VersionResolver` from `Services.cs`, add `: IVersionResolver`.

```csharp
using DotVer.Domain;

namespace DotVer.Application;

public sealed class VersionResolver : IVersionResolver
{
    // body verbatim from Services.cs
}
```

- [ ] **Step 3: Create `Services/InventoryService.cs`** — copy `InventoryService` from `Services.cs`, add `: IInventoryService`. Constructor parameters (`DotVerPaths`, `IEnvironmentContext`, `IConfigStore`) remain the same — all already interfaces or value objects.

- [ ] **Step 4: Create `Services/SelectionService.cs`** — copy `SelectionService` from `Services.cs`, add `: ISelectionService`. Update constructor parameters and private fields per the table above.

- [ ] **Step 5: Create `Services/InterceptorService.cs`** — copy `InterceptorService` from `Services.cs`, add `: IInterceptorService`. Update constructor parameters and private fields per the table above.

- [ ] **Step 6: Create `Services/ShimService.cs`** — copy `ShimService` from `Services.cs`, add `: IShimService`. No constructor parameter changes needed (takes `DotVerPaths` and `IEnvironmentContext`).

- [ ] **Step 7: Create `Services/EnvironmentBootstrapService.cs`** — copy `EnvironmentBootstrapService` from `Services.cs`, add `: IEnvironmentBootstrapService`. Update `_shimService` field type to `IShimService` and constructor parameter to `IShimService`.

- [ ] **Step 8: Create `Services/InstallService.cs`** — copy `InstallService` from `Services.cs`, add `: IInstallService`. Update `_inventoryService` field type to `IInventoryService` and constructor parameter to `IInventoryService`. Add `using System.Text.Json;`.

- [ ] **Step 9: Create `Services/StateService.cs`** — copy `StateService` from `Services.cs`, add `: IStateService`. Update `_versionResolver` and `_inventoryService` field types and constructor parameters per the table above.

- [ ] **Step 10: Verify Application project builds**

```bash
cd src/DotVer.Application && dotnet build
```

Expected: build succeeds. If duplicates error: confirm `Services.cs` still exists (do not delete it yet).

---

## Task 5: Split Infrastructure/Infrastructure.cs

**Files to create:**
- `src/DotVer.Infrastructure/SystemEnvironmentContext.cs`
- `src/DotVer.Infrastructure/JsonConfigStore.cs`
- `src/DotVer.Infrastructure/ProcessRunner.cs`
- `src/DotVer.Infrastructure/HttpResourceClient.cs`
- `src/DotVer.Infrastructure/DependencyInjection.cs`

**Source:** `src/DotVer.Infrastructure/Infrastructure.cs` — read it first.

- [ ] **Step 1: Create `SystemEnvironmentContext.cs`** — extract `SystemEnvironmentContext` verbatim.

```csharp
using System.Reflection;
using DotVer.Application;

namespace DotVer.Infrastructure;

public sealed class SystemEnvironmentContext : IEnvironmentContext
{
    // body verbatim from Infrastructure.cs
}
```

- [ ] **Step 2: Create `JsonConfigStore.cs`** — extract `JsonConfigStore` and keep `DotVerJsonContext` in the same file (it is internal to this class's serialization concern).

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Infrastructure;

public sealed class JsonConfigStore : IConfigStore
{
    // body verbatim from Infrastructure.cs
}

[JsonSerializable(typeof(DotVerConfig))]
internal sealed partial class DotVerJsonContext : JsonSerializerContext;
```

- [ ] **Step 3: Create `ProcessRunner.cs`** — extract `ProcessRunner` verbatim.

```csharp
using System.Diagnostics;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    // body verbatim from Infrastructure.cs
}
```

- [ ] **Step 4: Create `HttpResourceClient.cs`** — extract `HttpResourceClient` verbatim.

```csharp
using System.Net.Http.Headers;
using DotVer.Application;

namespace DotVer.Infrastructure;

public sealed class HttpResourceClient : IHttpResourceClient
{
    // body verbatim from Infrastructure.cs
}
```

- [ ] **Step 5: Create `DependencyInjection.cs`** — extract `ServiceCollectionExtensions` and update all DI registrations to map concrete types to their interfaces.

```csharp
using DotVer.Application;
using Microsoft.Extensions.DependencyInjection;

namespace DotVer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDotVer(this IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentContext, SystemEnvironmentContext>();
        services.AddSingleton(static provider => DotVerPaths.From(provider.GetRequiredService<IEnvironmentContext>()));
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddHttpClient<IHttpResourceClient, HttpResourceClient>();

        services.AddSingleton<IGlobalJsonService, GlobalJsonService>();
        services.AddSingleton<IVersionResolver, VersionResolver>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<ISelectionService, SelectionService>();
        services.AddSingleton<IInterceptorService, InterceptorService>();
        services.AddSingleton<IShimService, ShimService>();
        services.AddSingleton<IEnvironmentBootstrapService, EnvironmentBootstrapService>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<IStateService, StateService>();

        return services;
    }
}
```

- [ ] **Step 6: Verify Infrastructure project builds**

```bash
cd src/DotVer.Infrastructure && dotnet build
```

Expected: build succeeds.

---

## Task 6: Split Cli/Commands.cs + fix DashboardCommand

**Files to create:**
- `src/DotVer.Cli/Commands/CommandBase.cs`
- `src/DotVer.Cli/Commands/DashboardCommand.cs`
- `src/DotVer.Cli/Commands/ListCommand.cs`
- `src/DotVer.Cli/Commands/CurrentCommand.cs`
- `src/DotVer.Cli/Commands/UseCommand.cs`
- `src/DotVer.Cli/Commands/DefaultCommand.cs`
- `src/DotVer.Cli/Commands/AliasCommand.cs`
- `src/DotVer.Cli/Commands/UnaliasCommand.cs`
- `src/DotVer.Cli/Commands/InstallCommand.cs`
- `src/DotVer.Cli/Commands/ListRemoteCommand.cs`
- `src/DotVer.Cli/Commands/UninstallCommand.cs`
- `src/DotVer.Cli/Commands/DoctorCommand.cs`
- `src/DotVer.Cli/Commands/RegisterRootCommand.cs`
- `src/DotVer.Cli/Settings/ListSettings.cs`
- `src/DotVer.Cli/Settings/VersionTokenSettings.cs`
- `src/DotVer.Cli/Settings/AliasSettings.cs`
- `src/DotVer.Cli/Settings/UnaliasSettings.cs`
- `src/DotVer.Cli/Settings/InstallSettings.cs`
- `src/DotVer.Cli/Settings/DoctorSettings.cs`
- `src/DotVer.Cli/Settings/UninstallSettings.cs`
- `src/DotVer.Cli/Settings/RegisterRootSettings.cs`

**Source:** `src/DotVer.Cli/Commands.cs` — read it first.

All commands change constructor parameters from concrete service types to their interfaces.

**Constructor parameter changes for commands:**

| Command | Old type | New type |
|---|---|---|
| All commands | `InventoryService` | `IInventoryService` |
| All commands | `SelectionService` | `ISelectionService` |
| All commands | `StateService` | `IStateService` |
| All commands | `InstallService` | `IInstallService` |
| All commands | `EnvironmentBootstrapService` | `IEnvironmentBootstrapService` |

- [ ] **Step 1: Create `Commands/CommandBase.cs`**

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal abstract class AsyncCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected readonly IAnsiConsole Console;

    protected AsyncCommandBase(IAnsiConsole console)
    {
        Console = console;
    }

    protected static string Flag(bool value) => value ? "[green]yes[/]" : "[red]no[/]";
}

internal abstract class AsyncCommandBase : AsyncCommand
{
    protected readonly IAnsiConsole Console;

    protected AsyncCommandBase(IAnsiConsole console)
    {
        Console = console;
    }

    protected static string Flag(bool value) => value ? "[green]yes[/]" : "[red]no[/]";
}
```

- [ ] **Step 2: Create Settings files** — extract each `*Settings` class verbatim into its own file under `Settings/`. All settings files use:

```csharp
using Spectre.Console.Cli;

namespace DotVer.Cli;

// paste the class here
```

Files: `ListSettings.cs`, `VersionTokenSettings.cs`, `AliasSettings.cs`, `UnaliasSettings.cs`, `InstallSettings.cs`, `DoctorSettings.cs`, `UninstallSettings.cs`, `RegisterRootSettings.cs`.

- [ ] **Step 3: Create `Commands/DashboardCommand.cs`** — extract `DashboardCommand`. Update constructor parameters to use interfaces. Fix the `DashboardCommand` tight coupling: **replace** the `case "doctor":` block that does `new DoctorCommand(...).ExecuteAsync(...)` with an inline `RenderDoctorPanel()` call using the already-injected services:

```csharp
using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DashboardCommand : AsyncCommandBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ISelectionService _selectionService;
    private readonly IStateService _stateService;

    public DashboardCommand(
        IAnsiConsole console,
        IInventoryService inventoryService,
        ISelectionService selectionService,
        IStateService stateService)
        : base(console)
    {
        _inventoryService = inventoryService;
        _selectionService = selectionService;
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        while (true)
        {
            Console.Clear();

            var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
            var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
            var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

            Console.Write(
                new FigletText("dotver")
                    .Color(Color.SpringGreen2));

            var summary = new Grid();
            summary.AddColumn();
            summary.AddColumn();
            summary.AddRow("[grey]Current[/]", $"[aqua]{selection.Installation?.Version.Raw ?? "system"}[/]");
            summary.AddRow("[grey]Default[/]", $"[aqua]{config.DefaultVersion ?? "(unset)"}[/]");
            summary.AddRow("[grey]Reason[/]", selection.Reason);
            summary.AddRow("[grey]Managed[/]", inventory.Count(static sdk => sdk.IsManaged).ToString());

            Console.Write(new Panel(summary).Header("Status").BorderColor(Color.Grey));

            RenderInventoryTable(inventory);

            var action = Console.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Select an action[/]")
                    .PageSize(10)
                    .AddChoices("use", "install", "uninstall", "doctor", "refresh", "exit"));

            switch (action)
            {
                case "use":
                    var choices = inventory.Where(static sdk => sdk.CanBeUsedDirectly).Select(static sdk => sdk.Version.Raw).Distinct().ToArray();
                    if (choices.Length == 0)
                    {
                        Console.MarkupLine("[red]No directly selectable SDKs available.[/]");
                        Console.Input.ReadKey(false);
                        continue;
                    }

                    var selected = Console.Prompt(new SelectionPrompt<string>().Title("Version").AddChoices(choices));
                    await _stateService.SetCurrentAsync(selected, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "install":
                    Console.MarkupLine("[yellow]Use the `dotver install <version>` command for a full traceable installation.[/]");
                    Console.Input.ReadKey(false);
                    break;
                case "uninstall":
                    Console.MarkupLine("[yellow]Use the `dotver uninstall <version>` command to clean up a managed installation.[/]");
                    Console.Input.ReadKey(false);
                    break;
                case "doctor":
                    await RenderDoctorPanelAsync().ConfigureAwait(false);
                    Console.Input.ReadKey(false);
                    break;
                case "exit":
                    return 0;
            }
        }
    }

    private async Task RenderDoctorPanelAsync()
    {
        var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("SDKs detected", inventory.Count.ToString());
        grid.AddRow("Managed SDKs", inventory.Count(static sdk => sdk.IsManaged).ToString());
        grid.AddRow("Direct-select SDKs", inventory.Count(static sdk => sdk.CanBeUsedDirectly).ToString());
        grid.AddRow("Current", selection.Installation?.Version.Raw ?? "system");
        grid.AddRow("Default", config.DefaultVersion ?? "(unset)");
        grid.AddRow("Aliases", config.Aliases.Count.ToString());
        grid.AddRow("Warning", inventory.Any(static sdk => !sdk.CanBeUsedDirectly) ? "There are multi-SDK roots that serve only for detection/inspection." : "None");

        Console.Write(new Panel(grid).Header("Doctor").BorderColor(Color.Yellow));
    }

    private void RenderInventoryTable(IReadOnlyList<SdkInstallation> inventory)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("Version");
        table.AddColumn("Origin");
        table.AddColumn("Selectable");
        table.AddColumn("Host");

        foreach (var sdk in inventory)
        {
            table.AddRow(
                sdk.Version.Raw,
                sdk.Origin.ToString(),
                sdk.CanBeUsedDirectly ? "[green]yes[/]" : "[yellow]inspect-only[/]",
                sdk.HostRoot);
        }

        Console.Write(table);
    }
}
```

- [ ] **Step 4: Create remaining command files** — for each remaining command, extract verbatim from `Commands.cs` into its own file, update constructor parameters to use interfaces. Template:

```csharp
using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class XCommand : AsyncCommandBase<XSettings>
{
    // update concrete types → interfaces in constructor + fields
    // body verbatim otherwise
}
```

Commands to create: `ListCommand.cs`, `CurrentCommand.cs`, `UseCommand.cs`, `DefaultCommand.cs`, `AliasCommand.cs`, `UnaliasCommand.cs`, `InstallCommand.cs`, `ListRemoteCommand.cs`, `UninstallCommand.cs`, `DoctorCommand.cs`, `RegisterRootCommand.cs`.

- [ ] **Step 5: Verify CLI project builds**

```bash
cd src/DotVer.Cli && dotnet build
```

Expected: build succeeds.

---

## Task 7: Split Tests/DotVerTests.cs

**Files to create:**
- `tests/DotVer.Tests/Infrastructure/TestDoubles.cs`
- `tests/DotVer.Tests/Infrastructure/TestSandbox.cs`
- `tests/DotVer.Tests/Infrastructure/TestEnvironmentContext.cs`
- `tests/DotVer.Tests/Services/GlobalJsonServiceTests.cs`
- `tests/DotVer.Tests/Services/VersionResolverTests.cs`
- `tests/DotVer.Tests/Services/SelectionServiceTests.cs`
- `tests/DotVer.Tests/Services/InterceptorServiceTests.cs`
- `tests/DotVer.Tests/Services/StateServiceTests.cs`
- `tests/DotVer.Tests/Services/ShimServiceTests.cs`

**Source:** `tests/DotVer.Tests/DotVerTests.cs` — read it first.

All test namespaces stay `DotVer.Tests`. The test helper types (`InMemoryConfigStore`, `FakeProcessRunner`, `TestEnvironmentContext`, `TestSandbox`, `TestPlatform`) are used across multiple test files — put them in `Infrastructure/`.

- [ ] **Step 1: Create `Infrastructure/TestDoubles.cs`** — contains `InMemoryConfigStore`, `FakeProcessRunner`, and `TestPlatform` enum.

```csharp
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

internal sealed class InMemoryConfigStore : IConfigStore
{
    // verbatim from DotVerTests.cs
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    // verbatim from DotVerTests.cs
}

public enum TestPlatform { Windows, Linux, MacOS }
```

- [ ] **Step 2: Create `Infrastructure/TestEnvironmentContext.cs`** — contains `TestEnvironmentContext` verbatim.

```csharp
using DotVer.Application;

namespace DotVer.Tests;

internal sealed class TestEnvironmentContext : IEnvironmentContext
{
    // verbatim from DotVerTests.cs
}
```

- [ ] **Step 3: Create `Infrastructure/TestSandbox.cs`** — contains `TestSandbox` verbatim.

```csharp
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

internal sealed class TestSandbox : IDisposable
{
    // verbatim from DotVerTests.cs
}
```

- [ ] **Step 4: Create `Services/GlobalJsonServiceTests.cs`** — extract `GlobalJsonServiceTests` class verbatim.

```csharp
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class GlobalJsonServiceTests
{
    // verbatim from DotVerTests.cs
}
```

- [ ] **Step 5: Create `Services/VersionResolverTests.cs`** — extract `VersionResolverTests` class verbatim.

- [ ] **Step 6: Create `Services/SelectionServiceTests.cs`** — extract the selection-related test class (`SelectionAndInterceptTests`) and rename to `SelectionServiceTests` for consistency, keeping only the `CurrentVersionWinsOverGlobalJson` test. Add usings as needed.

- [ ] **Step 7: Create `Services/InterceptorServiceTests.cs`** — extract `InterceptorHandlesVersionAndListSdksWithoutCallingRealDotnet` test (currently in `SelectionAndInterceptTests`) into its own class `InterceptorServiceTests`.

- [ ] **Step 8: Create `Services/StateServiceTests.cs`** — extract `StateServiceTests` class verbatim.

- [ ] **Step 9: Create `Services/ShimServiceTests.cs`** — extract `ShimServiceTests` class verbatim (includes `InstallsWindowsShimsAndEnvSnippets`, `InstallsUnixShimsAndEnvSnippets`, `WindowsBootstrapUpdatesPowerShellProfiles`, `UnixBootstrapUpdatesShellProfiles`).

- [ ] **Step 10: Verify tests build and run**

```bash
cd tests/DotVer.Tests && dotnet test
```

Expected: all existing tests pass. If duplicates error, `DotVerTests.cs` still exists — do NOT delete it yet (Task 8 does that).

---

## Task 8: Delete monolithic files + final verification

This task runs after all previous tasks complete successfully.

- [ ] **Step 1: Delete original monolithic files**

```bash
rm src/DotVer.Domain/Models.cs
rm src/DotVer.Application/Abstractions.cs
rm src/DotVer.Application/Services.cs
rm src/DotVer.Infrastructure/Infrastructure.cs
rm src/DotVer.Cli/Commands.cs
rm tests/DotVer.Tests/DotVerTests.cs
```

- [ ] **Step 2: Full solution build**

```bash
dotnet build
```

Expected: 0 errors, 0 warnings related to missing types.

- [ ] **Step 3: Run all tests**

```bash
dotnet test
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: split monolithic files by responsibility and add service interfaces

- Domain: 13 type files + Enums/ subfolder
- Application: 5 abstraction files + 9 interface files + 9 service files
- Infrastructure: 5 focused files with interface-mapped DI
- Cli: 13 command files + 8 settings files; DashboardCommand no longer instantiates DoctorCommand directly
- Tests: 6 test files + 3 infrastructure helper files"
```
