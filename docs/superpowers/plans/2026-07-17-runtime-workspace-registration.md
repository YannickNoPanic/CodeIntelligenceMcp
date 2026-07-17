# Runtime Workspace Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add temporary per-session workspace registration to CodeIntelligenceMcp, plus an explicit save path for persisting registered code workspaces.

**Architecture:** Introduce a small workspace catalog abstraction that merges configured workspaces from `McpConfig` with runtime workspaces registered after startup. Existing providers continue to own indexing and cache lifetime; they only change their resolution source from `McpConfig.Workspaces` to the catalog. `WorkspaceManagementTool` becomes the user-facing surface for register, unregister, list, refresh, and save.

**Tech Stack:** .NET 10, C#, ModelContextProtocol, xUnit, FluentAssertions, existing `ToolResponses` JSON envelope.

## Global Constraints

- Tools in `CodeIntelligenceMcp/Tools/` stay thin: parameter validation and delegation only.
- All tool JSON responses go through `ToolResponses`.
- Use file-scoped namespaces and existing C# style.
- Always pass `CancellationToken` in async methods.
- No TODO comments.
- Runtime registration is temporary unless `save_workspace` is called explicitly.
- `save_workspace` rewrites the known `McpConfig` JSON schema and does not preserve comments.
- SqlSchemaMcp is out of scope for this plan; it needs a separate security-focused implementation pass.

---

## File Structure

- Create `src/CodeIntelligenceMcp/Workspaces/WorkspaceCatalog.cs`
  - Owns merged configured/runtime workspace lookup.
  - Validates runtime registration inputs.
  - Tracks `source` as `configured` or `runtime`.

- Create `src/CodeIntelligenceMcp/Config/McpConfigSource.cs`
  - Stores the startup config path for `save_workspace`.

- Modify `src/CodeIntelligenceMcp/Program.cs`
  - Register `WorkspaceCatalog` and `McpConfigSource` in DI.
  - Pass catalog into workspace providers.

- Modify `src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs`
  - Resolve workspaces through `WorkspaceCatalog`.

- Modify `src/CodeIntelligenceMcp/Workspaces/RoslynWorkspaceProvider.cs`
  - Constructor receives `WorkspaceCatalog`.

- Modify `src/CodeIntelligenceMcp/Workspaces/FileWalkWorkspaceProvider.cs`
  - Constructor receives `WorkspaceCatalog`.

- Modify `src/CodeIntelligenceMcp/Workspaces/WorkspaceAccess.cs`
  - Use `WorkspaceCatalog` to build known-workspace hints.

- Modify `src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs`
  - Add `register_workspace`, `unregister_workspace`, `save_workspace`.
  - Update `list_workspaces` to include `source`.

- Create `tests/CodeIntelligenceMcp.Tests/WorkspaceCatalogTests.cs`
  - Unit tests for runtime validation and lookup.

- Modify `tests/CodeIntelligenceMcp.Tests/WorkspaceAccessTests.cs`
  - Update helper setup for catalog-based hints.

- Create `tests/CodeIntelligenceMcp.Tests/WorkspaceManagementToolTests.cs`
  - Unit tests for list/register/unregister/save behavior.

- Modify `README.md`
  - Document runtime registration examples.

- Modify `docs/TOOLS.md`
  - Document new workspace management tools.

---

### Task 1: Workspace Catalog

**Files:**
- Create: `src/CodeIntelligenceMcp/Workspaces/WorkspaceCatalog.cs`
- Test: `tests/CodeIntelligenceMcp.Tests/WorkspaceCatalogTests.cs`

**Interfaces:**
- Consumes: `McpConfig`, `WorkspaceConfig`, `CleanArchitectureConfig`.
- Produces:
  - `WorkspaceCatalog(McpConfig config)`
  - `IReadOnlyList<WorkspaceCatalogEntry> List()`
  - `WorkspaceConfig? Resolve(string workspaceType, string workspace, Func<WorkspaceConfig, string?> getPath, Func<string, WorkspaceConfig> createAdHoc)`
  - `RegistrationResult Register(string name, string type, string path, CleanArchitectureConfig? cleanArchitecture = null)`
  - `bool Unregister(string name)`
  - `WorkspaceConfig? GetRuntime(string name)`
  - `bool IsRuntime(string name)`
  - `IReadOnlyList<string> KnownNames(string workspaceType)`

- [ ] **Step 1: Write catalog tests**

Create `tests/CodeIntelligenceMcp.Tests/WorkspaceCatalogTests.cs`:

```csharp
using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Workspaces;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class WorkspaceCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-ws").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void List_ConfiguredWorkspace_ReturnsConfiguredSource()
    {
        McpConfig config = ConfigWith(new WorkspaceConfig
        {
            Name = "configured",
            Type = "powershell",
            RootPath = _dir
        });
        var catalog = new WorkspaceCatalog(config);

        IReadOnlyList<WorkspaceCatalogEntry> entries = catalog.List();

        entries.Should().ContainSingle(e =>
            e.Workspace.Name == "configured"
            && e.Source == "configured"
            && e.Path == _dir);
    }

    [Fact]
    public void Register_ValidDotnetSolution_AddsRuntimeWorkspace()
    {
        string sln = Path.Combine(_dir, "App.slnx");
        File.WriteAllText(sln, string.Empty);
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("current", "dotnet", sln);

        result.Success.Should().BeTrue();
        catalog.List().Should().ContainSingle(e =>
            e.Workspace.Name == "current"
            && e.Workspace.Type == "dotnet"
            && e.Workspace.Solution == sln.Replace('\\', '/')
            && e.Source == "runtime");
    }

    [Fact]
    public void Register_ValidFileWalkRoot_AddsRuntimeWorkspace()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("scripts", "powershell", _dir);

        result.Success.Should().BeTrue();
        catalog.List().Should().ContainSingle(e =>
            e.Workspace.Name == "scripts"
            && e.Workspace.Type == "powershell"
            && e.Workspace.RootPath == _dir.Replace('\\', '/')
            && e.Source == "runtime");
    }

    [Fact]
    public void Register_DuplicateConfiguredName_Fails()
    {
        McpConfig config = ConfigWith(new WorkspaceConfig
        {
            Name = "current",
            Type = "powershell",
            RootPath = _dir
        });
        var catalog = new WorkspaceCatalog(config);

        RegistrationResult result = catalog.Register("current", "powershell", _dir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public void Register_NameWithPathSeparator_Fails()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("bad/name", "powershell", _dir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path separators");
    }

    [Fact]
    public void Register_UnknownType_Fails()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("current", "ruby", _dir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unknown type");
    }

    [Fact]
    public void Register_MissingDotnetPath_Fails()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("current", "dotnet", Path.Combine(_dir, "missing.sln"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public void Resolve_RuntimeName_ReturnsRuntimeWorkspace()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("scripts", "powershell", _dir);

        WorkspaceConfig? workspace = catalog.Resolve(
            "powershell",
            "scripts",
            ws => ws.RootPath,
            path => new WorkspaceConfig { Name = path, Type = "powershell", RootPath = path });

        workspace.Should().NotBeNull();
        workspace!.Name.Should().Be("scripts");
    }

    [Fact]
    public void Resolve_AbsolutePathMatchingRuntime_ReusesRuntimeName()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("scripts", "powershell", _dir);

        WorkspaceConfig? workspace = catalog.Resolve(
            "powershell",
            _dir,
            ws => ws.RootPath,
            path => new WorkspaceConfig { Name = path, Type = "powershell", RootPath = path });

        workspace.Should().NotBeNull();
        workspace!.Name.Should().Be("scripts");
    }

    [Fact]
    public void Unregister_RuntimeWorkspace_RemovesIt()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("scripts", "powershell", _dir);

        bool removed = catalog.Unregister("scripts");

        removed.Should().BeTrue();
        catalog.List().Should().NotContain(e => e.Workspace.Name == "scripts");
    }

    private static McpConfig ConfigWith(params WorkspaceConfig[] workspaces) => new()
    {
        Workspaces = [.. workspaces]
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter WorkspaceCatalogTests -v q --nologo
```

Expected: compile fails because `WorkspaceCatalog`, `WorkspaceCatalogEntry`, and `RegistrationResult` do not exist.

- [ ] **Step 3: Implement catalog**

Create `src/CodeIntelligenceMcp/Workspaces/WorkspaceCatalog.cs`:

```csharp
using System.Collections.Concurrent;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class WorkspaceCatalog(McpConfig config)
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet",
        "asp-classic",
        "powershell",
        "python",
        "javascript"
    };

    private readonly ConcurrentDictionary<string, WorkspaceConfig> _runtime =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkspaceCatalogEntry> List()
    {
        IEnumerable<WorkspaceCatalogEntry> configured = config.Workspaces
            .Select(w => new WorkspaceCatalogEntry(w, "configured", GetPath(w)));
        IEnumerable<WorkspaceCatalogEntry> runtime = _runtime.Values
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(w => new WorkspaceCatalogEntry(w, "runtime", GetPath(w)));

        return [.. configured.Concat(runtime)];
    }

    public WorkspaceConfig? Resolve(
        string workspaceType,
        string workspace,
        Func<WorkspaceConfig, string?> getPath,
        Func<string, WorkspaceConfig> createAdHoc)
    {
        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = NormalizePath(workspace);
            WorkspaceConfig? configured = All()
                .FirstOrDefault(w =>
                    w.Type == workspaceType
                    && getPath(w) is string p
                    && string.Equals(NormalizePath(p), normalizedPath, StringComparison.OrdinalIgnoreCase));

            return configured ?? createAdHoc(normalizedPath);
        }

        return All()
            .FirstOrDefault(w =>
                w.Name == workspace
                && w.Type == workspaceType
                && getPath(w) is not null);
    }

    public RegistrationResult Register(
        string name,
        string type,
        string path,
        CleanArchitectureConfig? cleanArchitecture = null)
    {
        string normalizedName = name.Trim();
        string normalizedType = type.Trim();
        string normalizedPath = NormalizePath(path.Trim());

        string? validationError = Validate(normalizedName, normalizedType, normalizedPath);
        if (validationError is not null)
            return RegistrationResult.Fail(validationError);

        WorkspaceConfig workspace = normalizedType.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new WorkspaceConfig
            {
                Name = normalizedName,
                Type = "dotnet",
                Solution = normalizedPath,
                CleanArchitecture = cleanArchitecture
            }
            : new WorkspaceConfig
            {
                Name = normalizedName,
                Type = normalizedType,
                RootPath = normalizedPath
            };

        return _runtime.TryAdd(normalizedName, workspace)
            ? RegistrationResult.Ok(workspace)
            : RegistrationResult.Fail($"workspace '{normalizedName}' already exists");
    }

    public bool Unregister(string name) => _runtime.TryRemove(name.Trim(), out _);

    public WorkspaceConfig? GetRuntime(string name) =>
        _runtime.TryGetValue(name.Trim(), out WorkspaceConfig? workspace) ? workspace : null;

    public bool IsRuntime(string name) => _runtime.ContainsKey(name.Trim());

    public IReadOnlyList<string> KnownNames(string workspaceType) =>
        [.. All()
            .Where(w => w.Type == workspaceType)
            .Select(w => w.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    private string? Validate(string name, string type, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "workspace name is required";

        if (name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            return "workspace name cannot contain path separators";

        if (!KnownTypes.Contains(type))
            return $"unknown type '{type}' — expected dotnet, asp-classic, powershell, python, or javascript";

        if (All().Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"workspace '{name}' already exists";

        if (type.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                return $"solution not found at '{path}'";

            string extension = Path.GetExtension(path);
            return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnf", StringComparison.OrdinalIgnoreCase)
                ? null
                : "dotnet workspace path must be a .sln, .slnx, or .slnf file";
        }

        return Directory.Exists(path) ? null : $"rootPath not found at '{path}'";
    }

    private IEnumerable<WorkspaceConfig> All() => config.Workspaces.Concat(_runtime.Values);

    private static string? GetPath(WorkspaceConfig workspace) => workspace.Solution ?? workspace.RootPath;

    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace('\\', '/');
}

internal sealed record WorkspaceCatalogEntry(WorkspaceConfig Workspace, string Source, string? Path);

internal sealed record RegistrationResult(bool Success, WorkspaceConfig? Workspace, string? Error)
{
    public static RegistrationResult Ok(WorkspaceConfig workspace) => new(true, workspace, null);
    public static RegistrationResult Fail(string error) => new(false, null, error);
}
```

- [ ] **Step 4: Run catalog tests**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter WorkspaceCatalogTests -v q --nologo
```

Expected: all `WorkspaceCatalogTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodeIntelligenceMcp/Workspaces/WorkspaceCatalog.cs tests/CodeIntelligenceMcp.Tests/WorkspaceCatalogTests.cs
git commit -m "feat: add runtime workspace catalog"
```

---

### Task 2: Provider Resolution Through Catalog

**Files:**
- Modify: `src/CodeIntelligenceMcp/Program.cs`
- Modify: `src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs`
- Modify: `src/CodeIntelligenceMcp/Workspaces/RoslynWorkspaceProvider.cs`
- Modify: `src/CodeIntelligenceMcp/Workspaces/FileWalkWorkspaceProvider.cs`
- Modify: `src/CodeIntelligenceMcp/Workspaces/WorkspaceAccess.cs`
- Modify: `tests/CodeIntelligenceMcp.Tests/WorkspaceAccessTests.cs`

**Interfaces:**
- Consumes: `WorkspaceCatalog` from Task 1.
- Produces:
  - `WorkspaceProviderBase<TIndex>(WorkspaceCatalog catalog, ILogger logger, string workspaceType)`
  - `WorkspaceAccess.GetAsync(..., WorkspaceCatalog catalog, string workspaceType, ...)`

- [ ] **Step 1: Update WorkspaceAccess tests first**

Replace `tests/CodeIntelligenceMcp.Tests/WorkspaceAccessTests.cs` with:

```csharp
using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Workspaces;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class WorkspaceAccessTests
{
    private sealed class ThrowingProvider(Exception ex) : IWorkspaceProvider<object>
    {
        public Task<object?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromException<object?>(ex);
        public bool Invalidate(string workspace) => false;
        public bool IsLoaded(string workspace) => false;
    }

    private sealed class NullProvider : IWorkspaceProvider<object>
    {
        public Task<object?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public bool Invalidate(string workspace) => false;
        public bool IsLoaded(string workspace) => false;
    }

    private static WorkspaceCatalog CatalogWith(params (string Name, string Type)[] workspaces) => new(new McpConfig
    {
        Workspaces = [.. workspaces.Select(w => new WorkspaceConfig { Name = w.Name, Type = w.Type })]
    });

    [Fact]
    public async Task GetAsync_UnknownWorkspace_ErrorListsKnownNames()
    {
        WorkspaceCatalog catalog = CatalogWith(("alpha", "dotnet"), ("beta", "dotnet"), ("legacy", "asp-classic"));

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new NullProvider(), catalog, "dotnet", "gamma", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("gamma").And.Contain("alpha").And.Contain("beta").And.NotContain("legacy");
    }

    [Fact]
    public async Task GetAsync_WorkspaceLoadException_SurfacesHintAndDetail()
    {
        var ex = new WorkspaceLoadException("solution failed to load", "install the .NET SDK", ["diag1"]);

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new ThrowingProvider(ex), CatalogWith(), "dotnet", "C:/x/y.sln", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("solution failed to load").And.Contain("install the .NET SDK").And.Contain("diag1");
    }

    [Fact]
    public async Task GetAsync_UnexpectedException_ShortMessageNoStackTrace()
    {
        var ex = new InvalidOperationException("boom");

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new ThrowingProvider(ex), CatalogWith(), "dotnet", "ws", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("boom").And.NotContain("   at ");
    }

    [Fact]
    public async Task GetAsync_Cancellation_Rethrows()
    {
        var ex = new OperationCanceledException();

        Func<Task> act = () => WorkspaceAccess.GetAsync(new ThrowingProvider(ex), CatalogWith(), "dotnet", "ws", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter WorkspaceAccessTests -v q --nologo
```

Expected: compile fails because `WorkspaceAccess.GetAsync` still takes `McpConfig`.

- [ ] **Step 3: Update WorkspaceAccess**

In `src/CodeIntelligenceMcp/Workspaces/WorkspaceAccess.cs`, change the signature and known-name lookup to:

```csharp
public static async Task<(TIndex? Index, string? Error)> GetAsync<TIndex>(
    IWorkspaceProvider<TIndex> provider,
    WorkspaceCatalog catalog,
    string workspaceType,
    string workspace,
    CancellationToken ct)
    where TIndex : class
{
    try
    {
        TIndex? index = await provider.GetAsync(workspace, ct);
        if (index is not null)
            return (index, null);

        string known = string.Join(", ", catalog.KnownNames(workspaceType));

        string hint = known.Length > 0
            ? $"known {workspaceType} workspaces: {known} — or pass an absolute path"
            : $"no {workspaceType} workspaces configured — pass an absolute path or register one with register_workspace";

        return (null, ToolResponses.Err($"workspace '{workspace}' not found", hint));
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (WorkspaceLoadException ex)
    {
        return (null, ToolResponses.Err(ex.Message, ex.Hint, ex.Detail));
    }
    catch (Exception ex)
    {
        return (null, ToolResponses.Err(
            $"failed to load workspace '{workspace}': {ex.Message}",
            "see the server log for details"));
    }
}
```

- [ ] **Step 4: Update provider base**

In `src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs`:

1. Change the primary constructor from `McpConfig config` to `WorkspaceCatalog catalog`.
2. Replace the `Resolve` method body with:

```csharp
private WorkspaceConfig? Resolve(string workspace)
{
    return catalog.Resolve(workspaceType, workspace, GetConfiguredPath, CreateAdHoc);
}
```

3. Replace the warning known-name expression in `GetAsync` with:

```csharp
string.Join(", ", catalog.KnownNames(workspaceType))
```

- [ ] **Step 5: Update provider constructors**

In `src/CodeIntelligenceMcp/Workspaces/RoslynWorkspaceProvider.cs`, change the constructor to:

```csharp
internal sealed class RoslynWorkspaceProvider(WorkspaceCatalog catalog, ILogger<RoslynWorkspaceProvider> logger)
    : WorkspaceProviderBase<RoslynWorkspaceIndex>(catalog, logger, "dotnet")
```

In `src/CodeIntelligenceMcp/Workspaces/FileWalkWorkspaceProvider.cs`, change the first constructor parameter to `WorkspaceCatalog catalog` and the base call to:

```csharp
: WorkspaceProviderBase<TIndex>(catalog, logger, workspaceType)
```

- [ ] **Step 6: Register catalog and update Program service wiring**

In `src/CodeIntelligenceMcp/Program.cs`, inside `RegisterServices` add:

```csharp
services.AddSingleton<WorkspaceCatalog>();
```

Then change all `new FileWalkWorkspaceProvider<TIndex>(config, ...)` calls to pass:

```csharp
sp.GetRequiredService<WorkspaceCatalog>()
```

as the first argument.

- [ ] **Step 7: Update tool call sites that use WorkspaceAccess**

In every tool class currently calling:

```csharp
WorkspaceAccess.GetAsync(provider, config, "<type>", workspace, ct)
```

change to:

```csharp
WorkspaceAccess.GetAsync(provider, catalog, "<type>", workspace, ct)
```

Add `WorkspaceCatalog catalog` to the primary constructor of:

- `src/CodeIntelligenceMcp/Tools/CSharpTools.cs`
- `src/CodeIntelligenceMcp/Tools/CodebaseWikiTool.cs`
- `src/CodeIntelligenceMcp/Tools/DiagnosticsTool.cs`
- `src/CodeIntelligenceMcp/Tools/ChangeAnalysisTool.cs`
- `src/CodeIntelligenceMcp/Tools/AspClassicTools.cs`
- `src/CodeIntelligenceMcp/Tools/SqlTools.cs`
- `src/CodeIntelligenceMcp/Tools/PowerShellTools.cs`
- `src/CodeIntelligenceMcp/Tools/PythonTools.cs`
- `src/CodeIntelligenceMcp/Tools/JsTools.cs`

Keep `McpConfig config` only in tool classes that still need it after Task 3.

- [ ] **Step 8: Run targeted tests**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter "WorkspaceAccessTests|WorkspaceCatalogTests" -v q --nologo
```

Expected: all targeted tests pass.

- [ ] **Step 9: Run build**

Run with isolated output if DLL locks occur:

```powershell
dotnet build src\CodeIntelligenceMcp -v q --nologo
```

If locked DLL errors occur, run:

```powershell
$out = Join-Path $env:TEMP 'CodeIntelligenceMcp-runtime-registration-build'
dotnet build src\CodeIntelligenceMcp -v q --nologo -p:UseAppHost=false -p:BaseOutputPath="$out\"
```

Expected: build succeeds.

- [ ] **Step 10: Commit**

```powershell
git add src/CodeIntelligenceMcp/Program.cs src/CodeIntelligenceMcp/Workspaces src/CodeIntelligenceMcp/Tools tests/CodeIntelligenceMcp.Tests/WorkspaceAccessTests.cs
git commit -m "refactor: resolve workspaces through runtime catalog"
```

---

### Task 3: Workspace Management Tools

**Files:**
- Modify: `src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs`
- Test: `tests/CodeIntelligenceMcp.Tests/WorkspaceManagementToolTests.cs`

**Interfaces:**
- Consumes: `WorkspaceCatalog` from Task 1 and provider invalidation from Task 2.
- Produces MCP tools:
  - `register_workspace(name, type, path, coreProject?, infraProject?, webProject?)`
  - `unregister_workspace(name)`
  - Updated `list_workspaces()` with `source`.

- [ ] **Step 1: Write management tool tests**

Create `tests/CodeIntelligenceMcp.Tests/WorkspaceManagementToolTests.cs`:

```csharp
using System.Text.Json;
using CodeIntelligenceMcp.AspClassic;
using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.JavaScript;
using CodeIntelligenceMcp.PowerShell;
using CodeIntelligenceMcp.Python;
using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Tools;
using CodeIntelligenceMcp.Workspaces;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class WorkspaceManagementToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-tool").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void ListWorkspaces_ConfiguredAndRuntime_IncludesSource()
    {
        McpConfig config = new()
        {
            Workspaces =
            [
                new WorkspaceConfig { Name = "configured", Type = "powershell", RootPath = _dir }
            ]
        };
        var catalog = new WorkspaceCatalog(config);
        catalog.Register("runtime", "powershell", _dir);
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.ListWorkspaces();

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement workspaces = doc.RootElement.GetProperty("workspaces");
        workspaces.EnumerateArray().Should().Contain(e =>
            e.GetProperty("name").GetString() == "configured"
            && e.GetProperty("source").GetString() == "configured");
        workspaces.EnumerateArray().Should().Contain(e =>
            e.GetProperty("name").GetString() == "runtime"
            && e.GetProperty("source").GetString() == "runtime");
    }

    [Fact]
    public void RegisterWorkspace_ValidDotnetSolution_ReturnsRegistered()
    {
        string sln = Path.Combine(_dir, "App.slnx");
        File.WriteAllText(sln, string.Empty);
        var catalog = new WorkspaceCatalog(new McpConfig());
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.RegisterWorkspace("current", "dotnet", sln);

        json.Should().Contain("\"registered\":true");
        catalog.GetRuntime("current").Should().NotBeNull();
    }

    [Fact]
    public void RegisterWorkspace_InvalidPath_ReturnsError()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.RegisterWorkspace("current", "dotnet", Path.Combine(_dir, "missing.sln"));

        json.Should().Contain("\"error\"");
        json.Should().Contain("not found");
    }

    [Fact]
    public void UnregisterWorkspace_RuntimeWorkspace_InvalidatesProviders()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("current", "powershell", _dir);
        var ps = new FakeProvider<PowerShellIndex>();
        WorkspaceManagementTool tool = CreateTool(catalog, ps: ps);

        string json = tool.UnregisterWorkspace("current");

        json.Should().Contain("\"unregistered\":true");
        ps.Invalidated.Should().Contain("current");
        catalog.GetRuntime("current").Should().BeNull();
    }

    [Fact]
    public void UnregisterWorkspace_ConfiguredWorkspace_ReturnsError()
    {
        McpConfig config = new()
        {
            Workspaces =
            [
                new WorkspaceConfig { Name = "configured", Type = "powershell", RootPath = _dir }
            ]
        };
        var catalog = new WorkspaceCatalog(config);
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.UnregisterWorkspace("configured");

        json.Should().Contain("\"error\"");
        json.Should().Contain("runtime");
    }

    private static WorkspaceManagementTool CreateTool(
        WorkspaceCatalog catalog,
        FakeProvider<RoslynWorkspaceIndex>? roslyn = null,
        FakeProvider<AspIndex>? asp = null,
        FakeProvider<PowerShellIndex>? ps = null,
        FakeProvider<PythonIndex>? py = null,
        FakeProvider<JsIndex>? js = null)
    {
        return new WorkspaceManagementTool(
            roslyn ?? new FakeProvider<RoslynWorkspaceIndex>(),
            asp ?? new FakeProvider<AspIndex>(),
            ps ?? new FakeProvider<PowerShellIndex>(),
            py ?? new FakeProvider<PythonIndex>(),
            js ?? new FakeProvider<JsIndex>(),
            catalog);
    }

    private sealed class FakeProvider<TIndex> : IWorkspaceProvider<TIndex>
        where TIndex : class
    {
        public List<string> Invalidated { get; } = [];

        public Task<TIndex?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromResult<TIndex?>(null);
        public bool IsLoaded(string workspace) => false;

        public bool Invalidate(string workspace)
        {
            Invalidated.Add(workspace);
            return true;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter WorkspaceManagementToolTests -v q --nologo
```

Expected: compile fails because the new constructor/signatures/tools are not implemented.

- [ ] **Step 3: Update WorkspaceManagementTool constructor**

Change the primary constructor in `src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs` to:

```csharp
public sealed class WorkspaceManagementTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslyn,
    IWorkspaceProvider<AspIndex> asp,
    IWorkspaceProvider<PowerShellIndex> ps,
    IWorkspaceProvider<PythonIndex> py,
    IWorkspaceProvider<JsIndex> js,
    WorkspaceCatalog catalog)
```

- [ ] **Step 4: Update list_workspaces**

Replace `ListWorkspaces` with:

```csharp
public string ListWorkspaces()
{
    var workspaces = catalog.List().Select(e => new
    {
        name = e.Workspace.Name,
        type = e.Workspace.Type,
        path = e.Path,
        source = e.Source,
        loaded = IsLoadedFor(e.Workspace)
    }).ToList();

    return ToolResponses.Ok(new { workspaces });
}
```

- [ ] **Step 5: Add register_workspace**

Add to `WorkspaceManagementTool`:

```csharp
[McpServerTool(Name = "register_workspace")]
[Description("Register a workspace for this MCP server process only. Use save_workspace to persist it.")]
public string RegisterWorkspace(
    [Description("Short workspace name, for example 'current'")] string name,
    [Description("Workspace type: dotnet, asp-classic, powershell, python, or javascript")] string type,
    [Description("Solution path for dotnet, root directory for file-walk workspace types")] string path,
    [Description("Optional Clean Architecture core project name for dotnet workspaces")] string? coreProject = null,
    [Description("Optional Clean Architecture infrastructure project name for dotnet workspaces")] string? infraProject = null,
    [Description("Optional Clean Architecture web/API project name for dotnet workspaces")] string? webProject = null)
{
    CleanArchitectureConfig? cleanArchitecture =
        string.IsNullOrWhiteSpace(coreProject)
        && string.IsNullOrWhiteSpace(infraProject)
        && string.IsNullOrWhiteSpace(webProject)
            ? null
            : new CleanArchitectureConfig
            {
                CoreProject = coreProject ?? string.Empty,
                InfraProject = infraProject ?? string.Empty,
                WebProject = webProject ?? string.Empty
            };

    RegistrationResult result = catalog.Register(name, type, path, cleanArchitecture);
    return result.Success
        ? ToolResponses.Ok(new
        {
            registered = true,
            name = result.Workspace!.Name,
            type = result.Workspace.Type,
            path = result.Workspace.Solution ?? result.Workspace.RootPath,
            source = "runtime"
        })
        : ToolResponses.Err(result.Error!);
}
```

- [ ] **Step 6: Add unregister_workspace**

Add:

```csharp
[McpServerTool(Name = "unregister_workspace")]
[Description("Remove a runtime workspace registration from this MCP server process.")]
public string UnregisterWorkspace(
    [Description("Runtime workspace name to remove")] string name)
{
    if (!catalog.IsRuntime(name))
        return ToolResponses.Err($"workspace '{name}' is not a runtime workspace");

    bool removed = catalog.Unregister(name);
    bool invalidated = roslyn.Invalidate(name) | asp.Invalidate(name)
        | ps.Invalidate(name) | py.Invalidate(name) | js.Invalidate(name);

    return ToolResponses.Ok(new { name, unregistered = removed, invalidated });
}
```

- [ ] **Step 7: Keep refresh_workspace unchanged except catalog constructor dependencies**

Confirm `RefreshWorkspace` still calls all provider `Invalidate` methods:

```csharp
bool any = roslyn.Invalidate(workspace) | asp.Invalidate(workspace)
    | ps.Invalidate(workspace) | py.Invalidate(workspace) | js.Invalidate(workspace);
return ToolResponses.Ok(new { workspace, refreshed = any });
```

- [ ] **Step 8: Run management tests**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter WorkspaceManagementToolTests -v q --nologo
```

Expected: all `WorkspaceManagementToolTests` pass.

- [ ] **Step 9: Run workspace tests together**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter "WorkspaceCatalogTests|WorkspaceManagementToolTests|WorkspaceAccessTests" -v q --nologo
```

Expected: all targeted tests pass.

- [ ] **Step 10: Commit**

```powershell
git add src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs tests/CodeIntelligenceMcp.Tests/WorkspaceManagementToolTests.cs
git commit -m "feat: register runtime workspaces"
```

---

### Task 4: Save Workspace and Documentation

**Files:**
- Create: `src/CodeIntelligenceMcp/Config/McpConfigSource.cs`
- Modify: `src/CodeIntelligenceMcp/Program.cs`
- Modify: `src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs`
- Modify: `tests/CodeIntelligenceMcp.Tests/WorkspaceManagementToolTests.cs`
- Modify: `README.md`
- Modify: `docs/TOOLS.md`

**Interfaces:**
- Consumes: runtime workspace support from Task 3.
- Produces:
  - `McpConfigSource(string Path)`
  - MCP tool `save_workspace(name)`

- [ ] **Step 1: Add save tests**

Append these tests to `WorkspaceManagementToolTests`:

```csharp
[Fact]
public void SaveWorkspace_RuntimeWorkspace_WritesConfigFile()
{
    string configPath = Path.Combine(_dir, "mcp-config.json");
    File.WriteAllText(configPath, """
        {
          "workspaces": []
        }
        """);
    string sln = Path.Combine(_dir, "App.slnx");
    File.WriteAllText(sln, string.Empty);
    var catalog = new WorkspaceCatalog(new McpConfig());
    catalog.Register("current", "dotnet", sln);
    WorkspaceManagementTool tool = CreateTool(catalog, configSource: new McpConfigSource(configPath));

    string json = tool.SaveWorkspace("current");

    json.Should().Contain("\"saved\":true");
    McpConfig saved = McpConfigLoader.Load(configPath);
    saved.Workspaces.Should().ContainSingle(w => w.Name == "current" && w.Solution == sln.Replace('\\', '/'));
}

[Fact]
public void SaveWorkspace_ConfiguredWorkspace_ReturnsError()
{
    string configPath = Path.Combine(_dir, "mcp-config.json");
    File.WriteAllText(configPath, """
        {
          "workspaces": []
        }
        """);
    McpConfig config = new()
    {
        Workspaces =
        [
            new WorkspaceConfig { Name = "configured", Type = "powershell", RootPath = _dir }
        ]
    };
    var catalog = new WorkspaceCatalog(config);
    WorkspaceManagementTool tool = CreateTool(catalog, configSource: new McpConfigSource(configPath));

    string json = tool.SaveWorkspace("configured");

    json.Should().Contain("\"error\"");
    json.Should().Contain("runtime");
}
```

Update the `CreateTool` helper signature to include:

```csharp
McpConfigSource? configSource = null
```

and pass:

```csharp
configSource ?? new McpConfigSource(Path.Combine(Path.GetTempPath(), "unused-mcp-config.json"))
```

to the `WorkspaceManagementTool` constructor.

- [ ] **Step 2: Run save tests to verify they fail**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter "SaveWorkspace" -v q --nologo
```

Expected: compile fails because `McpConfigSource` and `SaveWorkspace` do not exist.

- [ ] **Step 3: Create McpConfigSource**

Create `src/CodeIntelligenceMcp/Config/McpConfigSource.cs`:

```csharp
namespace CodeIntelligenceMcp.Config;

public sealed record McpConfigSource(string Path);
```

- [ ] **Step 4: Register config source**

In `src/CodeIntelligenceMcp/Program.cs`, after resolving `configPath`, register:

```csharp
services.AddSingleton(new McpConfigSource(configPath));
```

inside `RegisterServices`.

- [ ] **Step 5: Add config source to WorkspaceManagementTool constructor**

Change the constructor to include:

```csharp
McpConfigSource configSource)
```

- [ ] **Step 6: Add save_workspace implementation**

Add to `WorkspaceManagementTool`:

```csharp
[McpServerTool(Name = "save_workspace")]
[Description("Persist a runtime workspace to the mcp-config.json file used at startup.")]
public string SaveWorkspace(
    [Description("Runtime workspace name to persist")] string name)
{
    WorkspaceConfig? runtime = catalog.GetRuntime(name);
    if (runtime is null)
        return ToolResponses.Err($"workspace '{name}' is not a runtime workspace");

    McpConfig existing = McpConfigLoader.Load(configSource.Path);
    if (existing.Workspaces.Any(w => string.Equals(w.Name, runtime.Name, StringComparison.OrdinalIgnoreCase)))
        return ToolResponses.Err($"configured workspace '{runtime.Name}' already exists in '{configSource.Path}'");

    existing.Workspaces.Add(runtime);

    string json = JsonSerializer.Serialize(existing, ToolResponses.JsonOptions);
    File.WriteAllText(configSource.Path, json + Environment.NewLine);

    return ToolResponses.Ok(new
    {
        saved = true,
        name = runtime.Name,
        configPath = configSource.Path
    });
}
```

- [ ] **Step 7: Run save tests**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj --filter "SaveWorkspace" -v q --nologo
```

Expected: save tests pass.

- [ ] **Step 8: Update README**

Add this section under README "Available tools" notes or setup notes:

````markdown
### Temporary workspace registration

Agents can register a workspace for the current MCP process without editing `mcp-config.json`:

```json
{
  "name": "current",
  "type": "dotnet",
  "path": "C:/Git/MyApp/MyApp.slnx"
}
```

Then call existing tools with `"workspace": "current"`.

Use `save_workspace` only when you explicitly want to persist that runtime workspace to the
`mcp-config.json` file used at server startup. Runtime registrations disappear when the MCP
process exits.
````

- [ ] **Step 9: Update docs/TOOLS.md**

In the Workspace Management table, include:

```markdown
| `register_workspace` | Register a workspace for this MCP server process only. Existing tools can use the new short name immediately. | `name`; `type`; `path`; optional `coreProject`, `infraProject`, `webProject` |
| `unregister_workspace` | Remove a runtime workspace registration and invalidate any cached index for that name. | `name` |
| `save_workspace` | Persist a runtime workspace to the `mcp-config.json` file used at startup. | `name` |
```

Also update `list_workspaces` text to mention `source`.

- [ ] **Step 10: Run full tests**

Run:

```powershell
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj -v q --nologo
```

If locked DLL errors occur, run:

```powershell
$out = Join-Path $env:TEMP 'CodeIntelligenceMcp-runtime-registration-tests'
dotnet test tests\CodeIntelligenceMcp.Tests\CodeIntelligenceMcp.Tests.csproj -v q --nologo -p:UseAppHost=false -p:BaseOutputPath="$out\"
```

Expected: all tests pass.

- [ ] **Step 11: Publish and smoke test**

Run:

```powershell
dotnet publish src/CodeIntelligenceMcp -c Release -r win-x64 --self-contained false -o publish --nologo -v q
```

Then run a manual MCP smoke test that calls:

1. `register_workspace` with:
   - `name`: `current`
   - `type`: `dotnet`
   - `path`: `C:/Git/CodeIntelligenceMcp/CodeIntelligenceMcp.slnx`
2. `list_workspaces`
3. `get_codebase_wiki` with `workspace: "current"`

Expected:

- `register_workspace` returns `"registered": true`.
- `list_workspaces` includes `"current"` with `"source": "runtime"`.
- `get_codebase_wiki` returns markdown headed `# Codebase Wiki`.

- [ ] **Step 12: Commit**

```powershell
git add src/CodeIntelligenceMcp/Config/McpConfigSource.cs src/CodeIntelligenceMcp/Program.cs src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs tests/CodeIntelligenceMcp.Tests/WorkspaceManagementToolTests.cs README.md docs/TOOLS.md
git commit -m "feat: persist runtime workspaces on request"
```

---

## Self-Review

**Spec coverage:**
- Temporary per-session CodeIntelligence workspace registration: Tasks 1-3.
- Short name use in existing tools: Task 2 provider resolution plus Task 4 smoke test.
- `list_workspaces` source reporting: Task 3.
- Explicit persistence only: Task 4.
- SqlSchemaMcp deferred with credential safety: Global Constraints and no SqlSchemaMcp tasks.

**Placeholder scan:**
- No `TODO`, `TBD`, or "similar to" placeholders.
- Each implementation step has concrete file paths, signatures, commands, and expected outputs.

**Type consistency:**
- `WorkspaceCatalog`, `WorkspaceCatalogEntry`, `RegistrationResult`, and `McpConfigSource` names are consistent across tasks.
- `WorkspaceAccess.GetAsync` consistently takes `WorkspaceCatalog catalog`.
- `WorkspaceManagementTool` tests use the constructor shape introduced by the implementation tasks.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-17-runtime-workspace-registration.md`. Two execution options:

1. **Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
