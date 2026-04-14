# Design: Clean Architecture Refactor — DotVer

**Date:** 2026-04-14  
**Approach:** B — File split + service interfaces (DIP compliant)  
**Scope:** All layers — Domain, Application, Infrastructure, Cli, Tests

---

## Problem

Each layer is collapsed into a single monolithic file:

| File | Lines | Mixed responsibilities |
|---|---|---|
| `Services.cs` | 1030 | 9 distinct services |
| `Commands.cs` | 519 | 12 commands + settings + base classes |
| `Infrastructure.cs` | 184 | 5 infrastructure classes |
| `Models.cs` | 185 | 13 domain types |
| `DotVerTests.cs` | 423 | 5 test classes + test helpers |

Additional design violation: `DashboardCommand` instantiates `DoctorCommand` with `new`, bypassing DI.

---

## Goals

- One class/interface/record per file (SRP at file level)
- All Application services expose an interface (DIP)
- CLI commands depend on service interfaces, not concrete types
- DI registration updated to map interfaces to implementations
- Tests split by class; fakes/helpers in dedicated files
- Fix `DashboardCommand` tight coupling to `DoctorCommand`

---

## Target File Structure

### `DotVer.Domain/`

```
Enums/
  SdkOriginKind.cs
  ShellKind.cs
  SdkRollForward.cs
SdkVersion.cs
SdkInstallation.cs
SdkSelection.cs
DotVerConfig.cs
ReleaseChannel.cs
InstallPlan.cs
InstallOutcome.cs
UninstallOutcome.cs
EnvironmentBootstrapResult.cs
ProcessSpec.cs
ProcessResult.cs
ShimInstallResult.cs
GlobalJsonRequest.cs
```

`Models.cs` is deleted after migration. Enums move to `Enums/` subfolder.

### `DotVer.Application/`

```
Abstractions/
  IConfigStore.cs
  IEnvironmentContext.cs
  IProcessRunner.cs
  IHttpResourceClient.cs
  DotVerPaths.cs
Services/
  Interfaces/
    IGlobalJsonService.cs
    IVersionResolver.cs
    IInventoryService.cs
    ISelectionService.cs
    IInterceptorService.cs
    IShimService.cs
    IEnvironmentBootstrapService.cs
    IInstallService.cs
    IStateService.cs
  GlobalJsonService.cs
  VersionResolver.cs
  InventoryService.cs
  SelectionService.cs
  InterceptorService.cs
  ShimService.cs
  EnvironmentBootstrapService.cs
  InstallService.cs
  StateService.cs
```

`Services.cs` and `Abstractions.cs` are deleted after migration.

### `DotVer.Infrastructure/`

```
SystemEnvironmentContext.cs
JsonConfigStore.cs
ProcessRunner.cs
HttpResourceClient.cs
DependencyInjection.cs
```

`Infrastructure.cs` is deleted after migration.  
`DotVerJsonContext` stays internal to `JsonConfigStore.cs` (same file, private context).

### `DotVer.Cli/`

```
Commands/
  CommandBase.cs
  DashboardCommand.cs
  ListCommand.cs
  CurrentCommand.cs
  UseCommand.cs
  DefaultCommand.cs
  AliasCommand.cs
  UnaliasCommand.cs
  InstallCommand.cs
  ListRemoteCommand.cs
  UninstallCommand.cs
  DoctorCommand.cs
  RegisterRootCommand.cs
Settings/
  ListSettings.cs
  VersionTokenSettings.cs
  AliasSettings.cs
  UnaliasSettings.cs
  InstallSettings.cs
  DoctorSettings.cs
  UninstallSettings.cs
  RegisterRootSettings.cs
TypeRegistrar.cs
Program.cs
```

`Commands.cs` is deleted after migration.

### `DotVer.Tests/`

```
Services/
  GlobalJsonServiceTests.cs
  VersionResolverTests.cs
  SelectionServiceTests.cs
  InterceptorServiceTests.cs
  StateServiceTests.cs
  ShimServiceTests.cs
Infrastructure/
  TestDoubles.cs        (InMemoryConfigStore, FakeProcessRunner)
  TestSandbox.cs
  TestEnvironmentContext.cs
```

`DotVerTests.cs` is deleted after migration.

---

## Key Behavioral Changes

### 1. Service interfaces

Each Application service gets a matching interface in `Services/Interfaces/`. Example:

```csharp
// Services/Interfaces/IInventoryService.cs
public interface IInventoryService
{
    Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken);
    string? GetSystemDotnetPath();
}

// Services/Interfaces/IStateService.cs
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

Full interface list:
- `IGlobalJsonService` — `TryFind(string workingDirectory)`
- `IVersionResolver` — `ResolveToken`, `FindByToken`
- `IInventoryService` — `ScanAsync`, `GetSystemDotnetPath`
- `ISelectionService` — `ResolveAsync`
- `IInterceptorService` — `ExecuteAsync`
- `IShimService` — `InstallAsync`, `RenderEnvSnippet`
- `IEnvironmentBootstrapService` — `EnsureConfiguredAsync`
- `IInstallService` — `ResolveInstallPlanAsync`, `InstallAsync`, `ListRemoteAsync`
- `IStateService` — full CRUD on config state

### 2. DashboardCommand fix

`DashboardCommand` currently does:
```csharp
await new DoctorCommand(Console, _inventoryService, _selectionService, _stateService)
    .ExecuteAsync(context, new DoctorSettings());
```

Fix: inject `IDoctorCommand` — no, that creates an awkward Spectre dependency. Better fix: the Doctor rendering logic is simple (3 service calls + a grid). Extract it to a private `RenderDoctorPanel()` method inside `DashboardCommand` that uses its already-injected services. `DoctorCommand` keeps its own implementation independently.

### 3. DI registration (`DependencyInjection.cs`)

```csharp
public static IServiceCollection AddDotVer(this IServiceCollection services)
{
    // Infrastructure
    services.AddSingleton<IEnvironmentContext, SystemEnvironmentContext>();
    services.AddSingleton(sp => DotVerPaths.From(sp.GetRequiredService<IEnvironmentContext>()));
    services.AddSingleton<IConfigStore, JsonConfigStore>();
    services.AddSingleton<IProcessRunner, ProcessRunner>();
    services.AddHttpClient<IHttpResourceClient, HttpResourceClient>();

    // Application
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
```

### 4. CLI commands use interfaces

All commands change constructor signatures from concrete services to interfaces:

```csharp
// Before
public ListCommand(IAnsiConsole console, InventoryService inventoryService,
    SelectionService selectionService, StateService stateService)

// After
public ListCommand(IAnsiConsole console, IInventoryService inventoryService,
    ISelectionService selectionService, IStateService stateService)
```

### 5. Tests updated

- All existing test classes move to their own file under `Tests/Services/`
- `TestSandbox`, `TestEnvironmentContext`, `InMemoryConfigStore`, `FakeProcessRunner` move to `Tests/Infrastructure/`
- Test classes construct services using the same concrete types (no change to test logic), but service constructor parameters now accept interfaces — already satisfied since `InMemoryConfigStore : IConfigStore`, etc.
- `DotVerTests.cs` is deleted

---

## Files Deleted After Migration

| File | Reason |
|---|---|
| `src/DotVer.Domain/Models.cs` | Split into per-type files |
| `src/DotVer.Application/Services.cs` | Split into per-service files |
| `src/DotVer.Application/Abstractions.cs` | Split into `Abstractions/` subfolder |
| `src/DotVer.Infrastructure/Infrastructure.cs` | Split into per-class files |
| `src/DotVer.Cli/Commands.cs` | Split into `Commands/` + `Settings/` subfolders |
| `tests/DotVer.Tests/DotVerTests.cs` | Split into per-class files |

---

## Constraints

- No logic changes — behavior is identical after refactor
- No new NuGet packages
- All existing tests must pass after migration
- `DotVerJsonContext` stays private/internal (same file as `JsonConfigStore`)
- `DotVerPaths` stays in `Application/Abstractions/` (it is an application-level value object, not pure domain)
