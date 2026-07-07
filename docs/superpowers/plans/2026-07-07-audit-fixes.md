# Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the technical findings from `docs/product-audit-2026-07-06.md` — token-efficiency (caps, filters, relative paths), robustness (worktrees, errors, staleness, cancellation), coverage depth (derived types, transitive callers, multi-TFM, .slnf), DX (config discovery, first-run), and test coverage (fixture solution, analyzer tests).

**Architecture:** Introduce one shared response pipeline (`ToolResponses`) and one shared workspace-access helper (`WorkspaceAccess`) in the server project; all 10 tool classes delegate to them. Index-level changes (search filtering, wildcards, staleness cache, TFM dedup) live in `CodeIntelligenceMcp.Roslyn`. File-walk indexers get a shared `NameMatcher` in `CodeIntelligenceMcp.Common`. No new projects except test fixtures.

**Tech Stack:** .NET 10, ModelContextProtocol 1.1.0, Roslyn 5.3.0 (MSBuildWorkspace), LibGit2Sharp 0.31.0, xUnit + FluentAssertions.

## Global Constraints

- File-scoped namespaces, 4-space indent, CRLF, final newline (repo .editorconfig).
- Primary constructors where possible; `_camelCase` private fields only when a primary constructor cannot be used.
- Prefer pattern matching, `?.`/`??`; `var` only when the type is obvious.
- Structured logging only — no interpolation in log templates.
- No emojis anywhere. No TODO comments. No commented-out code in commits.
- Tests: xUnit + FluentAssertions, AAA with blank lines, names `Method_Scenario_ExpectedResult`. NSubstitute is being REMOVED in Task 17 — do not use it.
- Tools in `CodeIntelligenceMcp/Tools/` stay thin: delegate to index classes, no logic beyond parameter plumbing.
- Language indexer projects must not depend on each other. `.Roslyn` must NOT reference `.Common` (duplicate the tiny matcher instead — see Task 4).
- Build validation: `dotnet build src/CodeIntelligenceMcp` may fail with locked DLLs if an MCP server instance is running. NEVER kill running processes; instead validate with an isolated output dir: `dotnet build src/CodeIntelligenceMcp -o <scratchpad>/buildout`.
- Run tests with: `dotnet test tests/CodeIntelligenceMcp.Tests -v q --nologo` — all 101 existing tests must stay green.
- Response-envelope changes in this plan are breaking by design; there are no external users yet. Never introduce a second envelope style — everything goes through `ToolResponses`.
- Commit after each task with a conventional-commit message; end commit messages with the Co-Authored-By line from the harness rules.

---

## Phase 1 — Shared response pipeline (token efficiency)

### Task 1: `ToolResponses` shared serializer + migrate all tool classes

**Files:**
- Create: `src/CodeIntelligenceMcp/Tools/ToolResponses.cs`
- Modify: `src/CodeIntelligenceMcp/Tools/CSharpTools.cs`, `AspClassicTools.cs`, `SqlTools.cs`, `CodebaseWikiTool.cs`, `DiagnosticsTool.cs`, `ChangeAnalysisTool.cs`, `PowerShellTools.cs`, `PythonTools.cs`, `JsTools.cs`, `WorkspaceManagementTool.cs` (all in `src/CodeIntelligenceMcp/Tools/`)
- Test: `tests/CodeIntelligenceMcp.Tests/ToolResponsesTests.cs`

**Interfaces:**
- Produces (used by every later task):

```csharp
internal static class ToolResponses
{
    internal static readonly JsonSerializerOptions JsonOptions; // camelCase, relaxed escaping, WhenWritingNull
    public static string Ok(object result, bool stale = false);
    public static string OkList<T>(IReadOnlyList<T> items, int maxResults = 100, bool stale = false, string? hint = null);
    public static string Err(string message, string? hint = null, IReadOnlyList<string>? detail = null);
}
```

- `OkList` envelope: `{ "total": n, "returned": m, "truncated": bool, "stale": true?, "hint": "..."?, "items": [...] }` — `stale`/`hint` omitted when null/false (WhenWritingNull). `maxResults <= 0` means unlimited.
- `Ok` with `stale: true` wraps as `{ "stale": true, "hint": "index may be outdated — call refresh_workspace", "result": {...} }`; otherwise serializes the result object directly (unchanged shape).
- `Err`: `{ "error": "...", "hint": "..."?, "detail": [...]? }`.

- [ ] **Step 1: Write failing tests**

```csharp
namespace CodeIntelligenceMcp.Tests;

public sealed class ToolResponsesTests
{
    [Fact]
    public void OkList_MoreItemsThanMax_TruncatesAndReportsTotal()
    {
        IReadOnlyList<int> items = [.. Enumerable.Range(1, 250)];

        string json = ToolResponses.OkList(items, maxResults: 100);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(250);
        doc.RootElement.GetProperty("returned").GetInt32().Should().Be(100);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(100);
        doc.RootElement.GetProperty("hint").GetString().Should().Contain("maxResults");
    }

    [Fact]
    public void OkList_FewerItemsThanMax_NoTruncationFieldsNoise()
    {
        string json = ToolResponses.OkList<int>([1, 2], maxResults: 100);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        doc.RootElement.TryGetProperty("hint", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("stale", out _).Should().BeFalse();
    }

    [Fact]
    public void Err_QuotesInMessage_AreNotUnicodeEscaped()
    {
        string json = ToolResponses.Err("workspace 'x' not found");

        json.Should().Contain("'x'").And.NotContain("\\u0027");
    }

    [Fact]
    public void Ok_Stale_WrapsResultWithHint()
    {
        string json = ToolResponses.Ok(new { value = 1 }, stale: true);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("stale").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("value").GetInt32().Should().Be(1);
    }
}
```

`InternalsVisibleTo` for the tests project already exists on `.Roslyn`; add it for the server project: in `src/CodeIntelligenceMcp/CodeIntelligenceMcp.csproj` add `<InternalsVisibleTo Include="CodeIntelligenceMcp.Tests" />` inside an `<ItemGroup>`, and add a `<ProjectReference>` to `CodeIntelligenceMcp.csproj` in the test project if not present.

- [ ] **Step 2: Run tests, verify they fail** (`ToolResponses` does not exist)

- [ ] **Step 3: Implement `ToolResponses`**

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeIntelligenceMcp.Tools;

internal static class ToolResponses
{
    // Relaxed escaping: output goes to an LLM over stdio, never to HTML — escaping quotes
    // as ' would only waste tokens. WhenWritingNull drops empty optional fields.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string StaleHint = "index may be outdated — call refresh_workspace to rebuild";

    public static string Ok(object result, bool stale = false) =>
        stale
            ? JsonSerializer.Serialize(new { stale = true, hint = StaleHint, result }, JsonOptions)
            : JsonSerializer.Serialize(result, JsonOptions);

    public static string OkList<T>(IReadOnlyList<T> items, int maxResults = 100, bool stale = false, string? hint = null)
    {
        bool truncated = maxResults > 0 && items.Count > maxResults;
        var payload = new
        {
            total = items.Count,
            returned = truncated ? maxResults : items.Count,
            truncated,
            stale = stale ? (bool?)true : null,
            hint = hint ?? (truncated
                ? "truncated — refine the query or raise maxResults"
                : stale ? StaleHint : null),
            items = truncated ? items.Take(maxResults) : items
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string Err(string message, string? hint = null, IReadOnlyList<string>? detail = null) =>
        JsonSerializer.Serialize(new { error = message, hint, detail }, JsonOptions);
}
```

- [ ] **Step 4: Migrate all 10 tool classes** — delete each class's private `JsonOptions`/`Ok`/`Err` and replace call sites with `ToolResponses.Ok(...)` / `ToolResponses.Err(...)`. `CSharpTools.AmbiguityError` uses `ToolResponses.JsonOptions`. `CodebaseWikiTool`'s inline `JsonSerializer.Serialize(new { error = ... })` becomes `ToolResponses.Err(...)`. Do NOT switch list tools to `OkList` yet (Task 4 does that with `maxResults` params). Response bodies stay byte-compatible except: quotes unescaped, nulls dropped.

- [ ] **Step 5: Run all tests + build** — 101 + 4 new green. Build via isolated output dir if DLLs locked.

- [ ] **Step 6: Commit** — `feat: shared ToolResponses pipeline with relaxed escaping and list envelope`

### Task 2: `WorkspaceAccess` — actionable load errors + known-workspace list

**Files:**
- Create: `src/CodeIntelligenceMcp/Workspaces/WorkspaceAccess.cs`
- Create: `src/CodeIntelligenceMcp.Roslyn/WorkspaceLoadException.cs`
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynLoader.cs` (wrap MSBuildLocator + OpenSolutionAsync failures)
- Modify: all 10 tool classes (replace the `GetAsync` + null-check prologue)
- Test: `tests/CodeIntelligenceMcp.Tests/WorkspaceAccessTests.cs`

**Interfaces:**
- Produces:

```csharp
// .Roslyn project — carries a user-actionable hint across the boundary
public sealed class WorkspaceLoadException(string message, string? hint = null, IReadOnlyList<string>? detail = null)
    : Exception(message)
{
    public string? Hint { get; } = hint;
    public IReadOnlyList<string>? Detail { get; } = detail;
}

// Server project
internal static class WorkspaceAccess
{
    // Returns (index, null) on success or (null, errorJson) on any failure.
    public static async Task<(TIndex? Index, string? Error)> GetAsync<TIndex>(
        IWorkspaceProvider<TIndex> provider, McpConfig config, string workspaceType,
        string workspace, CancellationToken ct) where TIndex : class;
}
```

- Error contract: unknown workspace → `Err("workspace 'x' not found", hint: "known dotnet workspaces: a, b — or pass an absolute .sln/.slnx path")`. `WorkspaceLoadException` → `Err(message, ex.Hint, ex.Detail)`. Any other exception → `Err("failed to load workspace 'x': {ex.Message}", hint: "see the server log for details")` — never a raw stack trace, never the SDK's generic error.

- [ ] **Step 1: Write failing tests** (use a tiny fake provider class implementing `IWorkspaceProvider<object>` inline in the test file — no NSubstitute):

```csharp
public sealed class WorkspaceAccessTests
{
    private sealed class ThrowingProvider(Exception ex) : IWorkspaceProvider<object>
    {
        public Task<object?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromException<object?>(ex);
        public bool Invalidate(string workspace) => false;
    }

    private sealed class NullProvider : IWorkspaceProvider<object>
    {
        public Task<object?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public bool Invalidate(string workspace) => false;
    }

    private static McpConfig ConfigWith(params (string Name, string Type)[] ws) => /* build McpConfig with those workspaces */;

    [Fact]
    public async Task GetAsync_UnknownWorkspace_ErrorListsKnownNames()
    {
        McpConfig config = ConfigWith(("alpha", "dotnet"), ("beta", "dotnet"), ("legacy", "asp-classic"));

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new NullProvider(), config, "dotnet", "gamma", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("gamma").And.Contain("alpha").And.Contain("beta").And.NotContain("legacy");
    }

    [Fact]
    public async Task GetAsync_WorkspaceLoadException_SurfacesHintAndDetail()
    {
        var ex = new WorkspaceLoadException("solution failed to load", "install the .NET SDK", ["diag1"]);

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new ThrowingProvider(ex), ConfigWith(), "dotnet", "C:/x/y.sln", CancellationToken.None);

        error.Should().Contain("solution failed to load").And.Contain("install the .NET SDK").And.Contain("diag1");
    }

    [Fact]
    public async Task GetAsync_UnexpectedException_ShortMessageNoStackTrace()
    {
        var ex = new InvalidOperationException("boom");

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new ThrowingProvider(ex), ConfigWith(), "dotnet", "ws", CancellationToken.None);

        error.Should().Contain("boom").And.NotContain("at CodeIntelligenceMcp");
    }
}
```

Adjust `ConfigWith` to the actual `McpConfig`/`WorkspaceConfig` record shapes in `src/CodeIntelligenceMcp/Config/` (read them first). `OperationCanceledException` must be rethrown, not converted.

- [ ] **Step 2: Run tests, verify fail**

- [ ] **Step 3: Implement.** `WorkspaceAccess.GetAsync` wraps `provider.GetAsync`: null → build known-names error via `config.Workspaces.Where(w => w.Type == workspaceType)`; catch `WorkspaceLoadException` → `ToolResponses.Err(ex.Message, ex.Hint, ex.Detail)`; catch `OperationCanceledException` → rethrow; catch `Exception ex` → generic short error. In `RoslynLoader`: wrap `MSBuildLocator.RegisterDefaults()` in try/catch → `throw new WorkspaceLoadException("No compatible MSBuild/.NET SDK found for net10.0.", "Install the .NET 10 SDK, or pin a version with global.json. Visual Studio MSBuild also works.", [ex.Message])`. Wrap `OpenSolutionAsync` failures → `WorkspaceLoadException($"Failed to load solution '{path}': {ex.Message}", "Check that the solution builds with 'dotnet build' on this machine.", first 3 load diagnostics if available)`.

- [ ] **Step 4: Migrate tool prologues.** Every tool method's first lines become:

```csharp
(RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
if (index is null)
    return error!;
```

Each tool class gains an `McpConfig config` primary-constructor parameter (already a DI singleton). Workspace types: `"dotnet"` (CSharpTools, CodebaseWikiTool, DiagnosticsTool, ChangeAnalysisTool), `"asp-classic"` (AspClassicTools, SqlTools), `"powershell"`, `"python"`, `"javascript"`.

- [ ] **Step 5: Run all tests + build; commit** — `feat: actionable workspace load errors with known-workspace hints`

### Task 3: `list_workspaces` tool

**Files:**
- Modify: `src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs`
- Modify: `src/CodeIntelligenceMcp/Workspaces/IWorkspaceProvider.cs` + `WorkspaceProviderBase.cs` (expose loaded-state peek)

**Interfaces:**
- Produces: MCP tool `list_workspaces` (no params) → `{ "workspaces": [{ "name", "type", "path", "loaded": bool }] }` from `McpConfig`, plus a note that absolute `.sln/.slnx` paths work ad hoc.
- `IWorkspaceProvider<TIndex>` gains `bool IsLoaded(string workspace);` — `WorkspaceProviderBase` implements it as `_loaded.TryGetValue(key, out var l) && l.IsValueCreated && l.Value.IsCompletedSuccessfully` (key normalization identical to `Invalidate`).

- [ ] **Step 1: Implement tool** (thin, no test beyond compile — it is pure config projection):

```csharp
[McpServerTool(Name = "list_workspaces")]
[Description("List all configured workspaces with type, path, and whether they are already indexed. Absolute .sln/.slnx paths also work ad hoc on any dotnet tool.")]
public string ListWorkspaces()
{
    var workspaces = config.Workspaces.Select(w => new
    {
        w.Name,
        w.Type,
        path = w.Solution ?? w.RootPath,
        loaded = IsLoadedFor(w)
    }).ToList();
    return ToolResponses.Ok(new { workspaces });
}
```

`IsLoadedFor` dispatches on `w.Type` to the matching injected provider (the tool class already receives all five providers for `refresh_workspace`; check the real property names on `WorkspaceConfig` first).

- [ ] **Step 2: Build, run tests, commit** — `feat: list_workspaces tool with loaded-state`

### Task 4: Search quality — implicit-symbol filter, wildcards, maxResults everywhere

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (`SearchSymbol` ~line 409, `FindTypes` ~line 226)
- Create: `src/CodeIntelligenceMcp.Roslyn/SymbolQueryMatcher.cs`
- Create: `src/CodeIntelligenceMcp.Common/NameMatcher.cs` (same logic for file-walk indexers)
- Modify: `src/CodeIntelligenceMcp.JavaScript/JsIndex.cs`, `src/CodeIntelligenceMcp.Python/PythonIndex.cs`, `src/CodeIntelligenceMcp.PowerShell/PowerShellIndex.cs`, `src/CodeIntelligenceMcp.AspClassic/AspIndex.cs` (search methods use `NameMatcher`)
- Modify: all list-returning tools in `Tools/` — add `maxResults` param (default 100, `0 = unlimited`) and return `ToolResponses.OkList(results, maxResults)`. Affected: `search_symbol`, `find_types`, `find_usages`, `find_implementations`, `find_violations`, `find_dead_code`, `find_callers`, `get_complexity`, `get_coupling`, `asp_search`, `asp_find_symbol`, `js_search`, `js_find_function`, `js_find_class`, `py_search`, `py_find_function`, `py_find_class`, `ps_search`, `ps_find_function`.
- Test: `tests/CodeIntelligenceMcp.Tests/SymbolQueryMatcherTests.cs`, extend `RoslynLookupTests.cs`

**Interfaces:**
- Produces:

```csharp
// .Roslyn (do NOT reference .Common from .Roslyn — intentional duplication of ~20 lines)
internal static class SymbolQueryMatcher
{
    // '*' = any run, '?' = one char; no wildcards -> case-insensitive substring.
    public static bool Matches(string query, string candidate);
    public static bool HasWildcards(string query);
}
// .Common: public static class NameMatcher — identical members, used by the four file-walk indexers.
```

- `RoslynWorkspaceIndex.SearchSymbol(string query)` signature unchanged; skips implicit symbols.

- [ ] **Step 1: Write failing matcher tests**

```csharp
public sealed class SymbolQueryMatcherTests
{
    [Theory]
    [InlineData("Get*UseCase", "GetHowTosUseCase", true)]
    [InlineData("Get*UseCase", "CreateHowToUseCase", false)]
    [InlineData("I?seCase", "IUseCase", true)]
    [InlineData("usecase", "GetHowTosUseCase", true)]   // substring, case-insensitive
    [InlineData("usecase", "Repository", false)]
    public void Matches_WildcardAndSubstring_BehavesAsDocumented(string query, string candidate, bool expected)
    {
        SymbolQueryMatcher.Matches(query, candidate).Should().Be(expected);
    }
}
```

And in `RoslynLookupTests.cs` (existing `CreateForTesting` infrastructure) add:

```csharp
[Fact]
public void SearchSymbol_Record_DoesNotReturnCompilerGeneratedMembers()
{
    // Arrange: compilation containing: public record GetThingRequest(string Name);
    RoslynWorkspaceIndex index = CreateIndexWithSource("public record GetThingRequest(string Name);");

    var results = index.SearchSymbol("GetThing");

    results.Should().NotContain(r => r.SymbolName == "EqualityContract" || r.SymbolName.StartsWith("get_"));
    results.Should().Contain(r => r.SymbolName == "GetThingRequest");
}

[Fact]
public void SearchSymbol_WildcardQuery_MatchesGlobOnSymbolName()
{
    RoslynWorkspaceIndex index = CreateIndexWithSource(
        "public class GetHowTosUseCase { } public class CreateHowToUseCase { }");

    var results = index.SearchSymbol("Get*UseCase");

    results.Should().ContainSingle(r => r.SymbolName == "GetHowTosUseCase");
}
```

(Reuse the existing helper the file already has for building test compilations; match its name exactly.)

- [ ] **Step 2: Run, verify fail**

- [ ] **Step 3: Implement matcher**

```csharp
internal static class SymbolQueryMatcher
{
    public static bool HasWildcards(string query) => query.Contains('*') || query.Contains('?');

    public static bool Matches(string query, string candidate)
    {
        if (!HasWildcards(query))
            return candidate.Contains(query, StringComparison.OrdinalIgnoreCase);

        string pattern = "^" + Regex.Escape(query).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(candidate, pattern, RegexOptions.IgnoreCase);
    }
}
```

- [ ] **Step 4: Wire into `SearchSymbol`:** replace both `fqn.Contains(query, ...)` checks with: wildcard queries match on `Symbol.Name` via `SymbolQueryMatcher.Matches`, non-wildcard queries keep matching the FQN substring (current behavior). Skip implicit members: inside the member loop, before matching, `if (member.IsImplicitlyDeclared) continue;` and `if (member is IMethodSymbol m && m.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.LocalFunction)) continue;` then additionally skip `m.MethodKind == MethodKind.Constructor && m.IsImplicitlyDeclared`. Also skip property `EqualityContract` on records: `if (member is IPropertySymbol p && p.Name == "EqualityContract") continue;`. Wire `FindTypes.nameContains` through `SymbolQueryMatcher.Matches` too.

- [ ] **Step 5: `NameMatcher` in Common + wire the four file-walk search methods** (each currently uses `Contains(query, OrdinalIgnoreCase)` — mechanical swap; `asp_find_symbol` keeps its whole-word regex behavior for non-wildcard queries).

- [ ] **Step 6: Add `maxResults` to the listed tools.** Pattern per tool:

```csharp
[Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
```

and `return ToolResponses.OkList(results, maxResults);`. Update each tool's `[Description]` to mention wildcard support where the underlying search got it (`search_symbol`, `find_types`, `js/py/ps/asp` searches).

- [ ] **Step 7: Run all tests + build; commit** — `feat: wildcard search, implicit-symbol filtering, maxResults caps on all list tools`

### Task 5: Workspace-relative paths in responses

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (add root + `Rel` helper, apply in every response-model mapping)
- Modify: `src/CodeIntelligenceMcp.Roslyn/ReferenceQueries.cs`, `ComplexityAnalyzer.cs`, `CouplingAnalyzer.cs`, `RiskAnalyzer.cs`, `ViolationDetector.cs`, `ChangeAnalyzer.cs`, `PatternScanner.cs`, `WikiGenerator.cs` (wherever `FilePath` lands in a response model)
- Modify: file-walk indexers — verify they already emit root-relative paths (`SourceFileWalker` likely does; if so, no change)
- Test: extend `RoslynLookupTests.cs`

**Interfaces:**
- Produces: `RoslynWorkspaceIndex.RootDir` (solution directory, empty for `CreateForTesting`) and `internal string Rel(string absolutePath)` → forward-slash path relative to `RootDir`; returns input unchanged when `RootDir` is empty or the path is outside the root.
- Consumes: nothing new. Rule: **internal storage stays absolute** (analyzers read files from disk); only values copied into response models (`TypeSummary`, `SymbolSearchResult`, `ViolationResult`, `MethodInfo`, `UsageResult`, `CallerResult`, `MethodComplexity`, `TypeCoupling`, `HotspotResult`, `DeadCodeResult`, etc.) get relativized at mapping time.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void FindTypes_ReturnsWorkspaceRelativeForwardSlashPaths()
{
    // Arrange: index whose RootDir is set (extend CreateForTesting with an optional rootDir parameter)
    RoslynWorkspaceIndex index = CreateIndexWithSource("public class Foo { }", rootDir: @"C:\repo");
    // test compilation file paths must start with C:\repo\src\... for this to be meaningful;
    // give the syntax tree a path via CSharpSyntaxTree.ParseText(source, path: @"C:\repo\src\Foo.cs")

    var results = index.FindTypes(nameContains: "Foo");

    results.Single().FilePath.Should().Be("src/Foo.cs");
}
```

- [ ] **Step 2: Verify fail. Step 3: Implement.** `BuildAsync` computes `RootDir = Path.GetDirectoryName(solution.FilePath)`. `Rel`: `Path.GetRelativePath(RootDir, abs).Replace('\\', '/')`, guard `..` results (outside root → return absolute). Apply `Rel(...)` at every model-mapping site (grep `.FilePath` and the mapping constructors in the listed files). `analyze_file` input handling already accepts relative paths — unchanged.

- [ ] **Step 4: Run tests + build; commit** — `feat: workspace-relative paths in all Roslyn tool responses`

### Task 6: Staleness surfaced everywhere (cheap cached check)

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (add `IsStaleCached`)
- Modify: `src/CodeIntelligenceMcp/Tools/CSharpTools.cs`, `DiagnosticsTool.cs` (pass `stale:` into `Ok`/`OkList`)
- Test: none beyond compile (git-dependent; integration covered in Task 18)

**Interfaces:**
- Produces:

```csharp
// TTL-cached staleness: at most one git-status fingerprint per 10 seconds per index.
public bool IsStaleCached();
```

Implementation: `private readonly object _staleLock = new(); private DateTime _staleCheckedAtUtc; private bool _lastStale;` — inside a lock, recompute via existing `IsStale()` only when `DateTime.UtcNow - _staleCheckedAtUtc > TimeSpan.FromSeconds(10)`.

- [ ] **Step 1: Implement `IsStaleCached`.**
- [ ] **Step 2: Wire into tools:** every `CSharpTools`/`DiagnosticsTool` success return becomes `ToolResponses.Ok(x, index.IsStaleCached())` / `ToolResponses.OkList(x, maxResults, index.IsStaleCached())`. `CodebaseWikiTool` keeps its markdown banner but switches from `IsStale()` to `IsStaleCached()`; `ChangeAnalysisTool` keeps auto-refresh (uses real `IsStale()` — correctness matters there).
- [ ] **Step 3: Run tests + build; commit** — `feat: stale-index warnings on all dotnet tool responses`

## Phase 2 — Robustness

### Task 7: Git worktree support + branch fallbacks

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/Git/GitDiffService.cs`
- Test: `tests/CodeIntelligenceMcp.Tests/GitDiffServiceTests.cs` (new — uses real temp git repos via LibGit2Sharp, no shelling out)

**Interfaces:** public API unchanged; behavior fixes only.

- [ ] **Step 1: Failing tests**

```csharp
public sealed class GitDiffServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-git").FullName;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (UnauthorizedAccessException) { /* libgit2 pack files are read-only */ } }

    [Fact]
    public void ResolveRepoRoot_GitFileInsteadOfDirectory_ResolvesRoot()
    {
        // Arrange: simulate a worktree — .git is a FILE containing "gitdir: <path>"
        Repository.Init(_dir);
        string worktreeDir = Path.Combine(_dir, "wt"); Directory.CreateDirectory(worktreeDir);
        File.WriteAllText(Path.Combine(worktreeDir, ".git"), $"gitdir: {Path.Combine(_dir, ".git")}");

        string? root = GitDiffService.ResolveRepoRoot(Path.Combine(worktreeDir, "some.sln"));

        root.Should().Be(worktreeDir);
    }

    [Fact]
    public void ComputeFingerprint_EmptyRepoUnbornHead_ReturnsValueNotThrow()
    {
        Repository.Init(_dir);

        Action act = () => GitDiffService.ComputeFingerprint(_dir);

        act.Should().NotThrow();
    }

    [Fact]
    public void ResolveFromCommit_MainMissing_FallsBackToMasterViaGetChangedFiles()
    {
        // Arrange: init repo with default branch 'master', one commit
        Repository.Init(_dir);
        using var repo = new Repository(_dir);
        // create a file, stage, commit with a signature — then:
        Action act = () => GitDiffService.GetChangedFiles(_dir, "main");

        act.Should().NotThrow();
    }
}
```

(Fill in the commit-creation boilerplate: `Commands.Stage(repo, "*")`, `repo.Commit("init", sig, sig)` with `new Signature("t", "t@t", DateTimeOffset.Now)`.)

- [ ] **Step 2: Verify fail** (first test fails today: `.git` file is not recognized; second may throw NRE via `GetChangedFiles` paths — verify which fail and note it).

- [ ] **Step 3: Fix.** `ResolveRepoRoot`: `if (Directory.Exists(gitPath) || File.Exists(gitPath)) return dir;`. `GetChangedFiles`/`GetChangedFilesIncludingWorkingTree`/`GetFileContentAtBase`: guard `repo.Head.Tip is null` → throw `InvalidOperationException("Repository has no commits yet")` (callers already convert to short errors). `ResolveFromCommit`: after `main`/`origin/main` miss, try `master`/`origin/master`, then `repo.Head.TrackedBranch`; only then throw the existing `ArgumentException` — extend its message with available branch names (first 10).

- [ ] **Step 4: Run tests + build; commit** — `fix: git worktree root resolution, unborn HEAD guards, base-branch fallback`

### Task 8: Decouple shared index build from the first caller's cancellation

**Files:**
- Modify: `src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs:54-56`

**Interfaces:** unchanged.

- [ ] **Step 1: Change the Lazy factory** so the build runs on `CancellationToken.None`:

```csharp
Lazy<Task<TIndex>> lazy = _loaded.GetOrAdd(
    cacheKey,
    _ => new Lazy<Task<TIndex>>(() => LoadAsync(ws, CancellationToken.None)));
```

Update the comment above it: callers abandon via `WaitAsync(ct)`; the build itself always completes so parallel agents never lose a shared in-flight index to one client's timeout. The existing eviction of faulted builds stays.

- [ ] **Step 2: Run tests + build; commit** — `fix: shared index build no longer cancelled by first caller`

### Task 9: Log rotation + full exception detail

**Files:**
- Modify: `src/CodeIntelligenceMcp/Logging/FileLoggerProvider.cs`
- Test: `tests/CodeIntelligenceMcp.Tests/FileLoggerProviderTests.cs`

**Interfaces:** ctor unchanged. New behavior: on open, if the file exceeds 10 MB, move it to `<name>.old` (replace existing). Exceptions log `exception.ToString()` (type + message + stack), timestamps via `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void Ctor_ExistingFileOverSizeCap_RollsToOld()
{
    string path = Path.Combine(Directory.CreateTempSubdirectory("citest-log").FullName, "t.log");
    File.WriteAllBytes(path, new byte[11 * 1024 * 1024]);

    using var provider = new FileLoggerProvider(path);

    File.Exists(path + ".old").Should().BeTrue();
    new FileInfo(path).Length.Should().BeLessThan(1024);
}

[Fact]
public void Log_WithException_WritesStackTrace()
{
    string path = Path.Combine(Directory.CreateTempSubdirectory("citest-log").FullName, "t.log");
    Exception caught;
    try { throw new InvalidOperationException("boom"); } catch (Exception ex) { caught = ex; }

    using (var provider = new FileLoggerProvider(path))
        provider.CreateLogger("T").LogError(caught, "failed");

    File.ReadAllText(path).Should().Contain("boom").And.Contain("at CodeIntelligenceMcp.Tests");
}
```

- [ ] **Step 2: Verify fail. Step 3: Implement** (roll in ctor before opening the stream; `File.Move(path, path + ".old", overwrite: true)`; in the log method replace the message-only exception line with `exception.ToString()`; use `CultureInfo.InvariantCulture` for the timestamp).

- [ ] **Step 4: Run tests + build; commit** — `fix: log rotation at 10MB and full exception stack traces`

### Task 10: Config discovery + resilient first run

**Files:**
- Modify: `src/CodeIntelligenceMcp/Program.cs` (config path resolution, missing-config tolerance)
- Modify: `src/CodeIntelligenceMcp/Config/McpConfigLoader.cs` (friendlier JSON errors)
- Modify: `src/CodeIntelligenceMcp/CodeIntelligenceMcp.csproj` (conditional Content include)
- Test: `tests/CodeIntelligenceMcp.Tests/McpConfigLoaderTests.cs` (new)

**Interfaces:**
- Config path resolution order: `--config <path>` CLI arg → `CODEINTEL_CONFIG` env var → `AppContext.BaseDirectory/mcp-config.json`.
- Missing config file is **no longer fatal**: log a warning, continue with an empty workspace list (ad-hoc absolute paths still work on every dotnet tool). Malformed JSON stays fatal but with message `"mcp-config.json is invalid JSON: {ex.Message} (path: {path})"`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void Load_MissingFile_ReturnsEmptyConfigInsteadOfThrowing()
{
    McpConfig config = McpConfigLoader.Load(@"C:\does\not\exist.json", NullLogger.Instance);

    config.Workspaces.Should().BeEmpty();
}

[Fact]
public void Load_InvalidJson_ThrowsWithPathInMessage()
{
    string path = Path.Combine(Directory.CreateTempSubdirectory("citest-cfg").FullName, "bad.json");
    File.WriteAllText(path, "{ not json");

    Action act = () => McpConfigLoader.Load(path, NullLogger.Instance);

    act.Should().Throw<InvalidOperationException>().WithMessage("*invalid JSON*").WithMessage($"*{path}*");
}
```

- [ ] **Step 2: Verify fail. Step 3: Implement loader changes** (missing → warn + `new McpConfig { Workspaces = [] }` matching the actual record shape; wrap `JsonException` into `InvalidOperationException` with path).

- [ ] **Step 4: Program.cs resolution**

```csharp
static string ResolveConfigPath(string[] args)
{
    int i = Array.IndexOf(args, "--config");
    if (i >= 0 && i + 1 < args.Length)
        return Path.GetFullPath(args[i + 1]);
    return Environment.GetEnvironmentVariable("CODEINTEL_CONFIG") is { Length: > 0 } env
        ? Path.GetFullPath(env)
        : Path.Combine(AppContext.BaseDirectory, "mcp-config.json");
}
```

- [ ] **Step 5: csproj** — make the Content include conditional so a clean clone builds:

```xml
<Content Include="..\..\mcp-config.json" Link="mcp-config.json" Condition="Exists('..\..\mcp-config.json')">
```

- [ ] **Step 6: Run all tests; build once from a simulated clean state** (temporarily rename repo-root `mcp-config.json`, `dotnet build -o <scratchpad>/cleanbuild`, rename back — do this carefully and restore even on failure).

- [ ] **Step 7: Commit** — `feat: config via --config/CODEINTEL_CONFIG, non-fatal missing config, clean-clone build`

## Phase 3 — Coverage depth

### Task 11: `find_derived_types` + generic-aware `implementsInterface`

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (new `FindDerivedTypes`, extend interface matching)
- Modify: `src/CodeIntelligenceMcp/Tools/CSharpTools.cs` (new tool)
- Test: extend `tests/CodeIntelligenceMcp.Tests/RoslynLookupTests.cs`

**Interfaces:**
- Produces:

```csharp
public IReadOnlyList<ImplementationSummary> FindDerivedTypes(string baseTypeName);
// walks each type's BaseType chain; matches simple name (OrdinalIgnoreCase) or FQN
```

- Tool `find_derived_types(workspace, baseTypeName, maxResults=100)` → `OkList` of `ImplementationSummary`.
- Interface matching upgrade (in `FindTypes` and `FindImplementations`): if the query contains `<`, match against `i.ToDisplayString()` with whitespace normalized (`"IUseCase<CreateXxxRequest, Result>"` matches `Ns.IUseCase<Ns2.CreateXxxRequest, Ns3.Result>` by comparing the generic name + arity + simple names of type args); otherwise keep simple-name matching. Concretely: parse the query as `Name` + comma-split args inside `<...>`; a candidate `INamedTypeSymbol i` matches when `i.Name == parsedName && i.TypeArguments.Length == args.Length && args.Zip(i.TypeArguments).All(p => p.Second.Name.Equals(p.First.Trim(), OrdinalIgnoreCase))`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void FindDerivedTypes_AbstractBase_ReturnsAllDescendants()
{
    RoslynWorkspaceIndex index = CreateIndexWithSource(
        "public abstract class BaseHandler { } public class EmailHandler : BaseHandler { } public class SmsHandler : EmailHandler { } public class Unrelated { }");

    var results = index.FindDerivedTypes("BaseHandler");

    results.Select(r => r.TypeName).Should().BeEquivalentTo(["EmailHandler", "SmsHandler"]);
}

[Fact]
public void FindTypes_GenericInterfaceWithArgs_MatchesExactConstruction()
{
    RoslynWorkspaceIndex index = CreateIndexWithSource(
        "public interface IUseCase<TIn, TOut> { } public record ReqA; public record ReqB; " +
        "public class UseCaseA : IUseCase<ReqA, int> { } public class UseCaseB : IUseCase<ReqB, int> { }");

    var results = index.FindTypes(implementsInterface: "IUseCase<ReqA, int>");

    results.Select(r => r.Name).Should().BeEquivalentTo(["UseCaseA"]);
}
```

- [ ] **Step 2: Verify fail. Step 3: Implement** (BaseType-chain walk: `for (var b = t.Symbol.BaseType; b is not null; b = b.BaseType)`; extract the generic-matching into a private static helper used by both `FindTypes` and `FindImplementations`). Add the tool method following the exact pattern of `find_implementations`.

- [ ] **Step 4: Run tests + build; commit** — `feat: find_derived_types tool and generic-argument-aware interface matching`

### Task 12: Transitive `find_callers` + all-overload references

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/ReferenceQueries.cs` (`FindCallersAsync` ~line 58)
- Modify: `src/CodeIntelligenceMcp.Roslyn/Models/CallerResult.cs` (add `int Depth`)
- Modify: `src/CodeIntelligenceMcp/Tools/CSharpTools.cs` (`find_callers` gains `depth` param, default 1, clamp 1..3)
- Test: integration-covered in Task 18 (SymbolFinder needs a real Solution — `CreateForTesting` cannot exercise this; note this in the test plan)

**Interfaces:**
- `FindCallersAsync(string typeName, string methodName, int depth = 1, CancellationToken ct = default)`; result records carry `Depth` (1 = direct caller).
- All ordinary overloads of the named method are searched (union of `SymbolFinder.FindReferencesAsync` over each), not just the first.

- [ ] **Step 1: Implement.** BFS: level 1 = current logic over all overloads; for levels 2..depth, take each distinct caller's `IMethodSymbol` (resolvable from the enclosing method syntax via the semantic model — extend the existing enclosing-method walk to also return the symbol) and run `FindReferencesAsync` on those. Deduplicate on `(callerType, callerMethod, file, line)`. Cap the BFS frontier at 200 methods per level to bound cost.
- [ ] **Step 2: Tool param:**

```csharp
[Description("Transitive depth: 1 = direct callers only (default), up to 3 = callers-of-callers")] int depth = 1,
```

clamp with `Math.Clamp(depth, 1, 3)`.
- [ ] **Step 3: Run tests + build (existing 101 stay green); commit** — `feat: transitive find_callers with depth and all-overload coverage`

### Task 13: Multi-TFM project dedup

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (`BuildAsync` project loop, ~line 115-165)
- Test: extend `RoslynLookupTests.cs` (two compilations, same assembly name)

**Interfaces:** unchanged. Behavior: when a solution multi-targets, MSBuildWorkspace yields one `Project` per TFM named `Name(net8.0)` etc. Rule: group `solution.Projects` by `Project.FilePath`; index only the first of each group; strip a trailing `(...)` suffix from `Project.Name` for `ProjectName` so filters and wiki output see one logical project.

- [ ] **Step 1: Failing test** (unit-level approximation via `CreateForTesting` is not possible for project grouping — instead test the name normalization helper):

```csharp
[Theory]
[InlineData("Datalake2.Core(net8.0)", "Datalake2.Core")]
[InlineData("Datalake2.Core", "Datalake2.Core")]
public void NormalizeProjectName_StripsTfmSuffix(string input, string expected)
{
    RoslynWorkspaceIndex.NormalizeProjectName(input).Should().Be(expected);
}
```

Make `NormalizeProjectName` an `internal static` method on `RoslynWorkspaceIndex`: strip a trailing `(...)` group only if it looks like a TFM (`net`, `netstandard`, `netcoreapp` prefix inside the parens).

- [ ] **Step 2: Verify fail. Step 3: Implement** — in `BuildAsync`: `HashSet<string> seenProjectFiles` (OrdinalIgnoreCase); `continue` on duplicate `project.FilePath`; use `NormalizeProjectName(project.Name)` for `IndexedType.ProjectName`. Apply the same normalization in `GetProjectDependencies` so the graph doesn't show TFM duplicates.

- [ ] **Step 4: Run tests + build; commit** — `fix: deduplicate multi-targeted projects and strip TFM suffixes`

### Task 14: `.slnf` solution filter support

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynLoader.cs`
- Create: `src/CodeIntelligenceMcp.Roslyn/SolutionFilterFile.cs`
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (`BuildAsync` gains optional project allowlist)
- Modify: tool `[Description]`s mentioning `.sln/.slnx` → `.sln/.slnx/.slnf` (all tool classes — mechanical find/replace)
- Test: `tests/CodeIntelligenceMcp.Tests/SolutionFilterFileTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record SolutionFilterFile(string SolutionPath, IReadOnlySet<string> ProjectPaths)
{
    // Parses the .slnf JSON: { "solution": { "path": "..\\X.sln", "projects": ["src\\A\\A.csproj", ...] } }
    // Paths are resolved absolute against the .slnf location, normalized to forward slashes, OrdinalIgnoreCase set.
    public static SolutionFilterFile Parse(string slnfPath);
}
```

- `RoslynWorkspaceIndex.BuildAsync(..., IReadOnlySet<string>? projectAllowlist = null, ...)` — when set, skip `Project`s whose `FilePath` (normalized) is not in the set.
- `RoslynLoader`: when the path ends with `.slnf` (OrdinalIgnoreCase), `Parse` it, open the referenced `.sln`, pass the allowlist. Unknown extensions (not `.sln/.slnx/.slnf`) → `WorkspaceLoadException($"'{path}' is not a .sln, .slnx, or .slnf file")`.

- [ ] **Step 1: Failing parse test**

```csharp
[Fact]
public void Parse_RelativePaths_ResolvedAgainstSlnfDirectory()
{
    string dir = Directory.CreateTempSubdirectory("citest-slnf").FullName;
    string slnf = Path.Combine(dir, "part.slnf");
    File.WriteAllText(slnf, """{ "solution": { "path": "sub\\Full.sln", "projects": [ "sub\\src\\A\\A.csproj" ] } }""");

    SolutionFilterFile parsed = SolutionFilterFile.Parse(slnf);

    parsed.SolutionPath.Should().Be(Path.Combine(dir, "sub", "Full.sln"));
    parsed.ProjectPaths.Should().ContainSingle().Which.Should().EndWith("A/A.csproj");
}
```

- [ ] **Step 2: Verify fail. Step 3: Implement parser, loader branch, BuildAsync allowlist, description updates.**
- [ ] **Step 4: Run tests + build; commit** — `feat: .slnf solution filter support`

### Task 15: Partial classes — prefer hand-written declaration location

**Files:**
- Modify: `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (the `Locations.FirstOrDefault(l => l.IsInSource)` sites in `BuildAsync` ~line 132 and `CreateForTesting` ~line 92)
- Test: extend `RoslynLookupTests.cs`

**Interfaces:** unchanged. Rule: among a type's source locations, prefer the first whose file path does not end in `.g.cs`/`.generated.cs` and is not inside `obj/`; fall back to the first source location. Extract as `internal static Location? PickPrimaryLocation(INamedTypeSymbol type)` used by both build paths.

- [ ] **Step 1: Failing test** — two syntax trees declaring `partial class Split`, first tree path `C:\repo\obj\Split.g.cs`, second `C:\repo\src\Split.cs`; assert `GetType("Split")!.FilePath` ends with `src/Split.cs`.
- [ ] **Step 2: Verify fail. Step 3: Implement. Step 4: Run + build; commit** — `fix: partial types report the hand-written declaration location`

### Task 16: Ad-hoc path reuses configured workspace cache

**Files:**
- Modify: `src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs` (`GetAsync` rooted-path branch)

**Interfaces:** unchanged. Rule: when a rooted path normalizes (forward slashes, OrdinalIgnoreCase) to the same value as a configured workspace's path for this type, use that workspace's config (and thus its cache key) instead of creating an ad-hoc duplicate.

- [ ] **Step 1: Implement** in the `Path.IsPathRooted` branch:

```csharp
string normalizedPath = workspace.Replace('\\', '/');
WorkspaceConfig? configured = config.Workspaces.FirstOrDefault(w =>
    w.Type == workspaceType
    && GetConfiguredPath(w) is string p
    && string.Equals(p.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase));
ws = configured ?? CreateAdHoc(normalizedPath);
```

- [ ] **Step 2: Run tests + build; commit** — `fix: absolute path to a configured solution reuses its index`

### Task 17: Small fixes — serialization typo, dead dependency, floating version

**Files:**
- Modify: `src/CodeIntelligenceMcp/Tools/PowerShellTools.cs:66` (`hassCmdletBinding` → `hasCmdletBinding` — check whether it is an anonymous-object property name and fix the spelling)
- Modify: `tests/CodeIntelligenceMcp.Tests/CodeIntelligenceMcp.Tests.csproj` (remove the NSubstitute `PackageReference` — verify zero usages first with grep)
- Modify: `src/CodeIntelligenceMcp.Python/CodeIntelligenceMcp.Python.csproj` (Tomlyn `0.17.*` → pin the currently restored version; check `obj/project.assets.json` for the resolved number)

- [ ] **Step 1: Make the three changes. Step 2: Run tests + build. Step 3: Commit** — `chore: fix hasCmdletBinding typo, drop unused NSubstitute, pin Tomlyn`

## Phase 4 — Tests

### Task 18: Fixture solution + end-to-end integration tests

**Files:**
- Create: `tests/fixtures/FixtureSolution/FixtureSolution.sln` with three projects:
  - `Fixture.Core/Fixture.Core.csproj` (`net10.0`, no deps) — `IGreetUseCase.cs` (`public interface IGreetUseCase { string Greet(string name); }`), `GreetUseCase.cs` (implements it), `HttpViolation.cs` (`public class HttpViolation { private readonly System.Net.Http.HttpClient _client = new(); }` — deliberate `core-no-http` + `direct-instantiation` bait)
  - `Fixture.Infrastructure/Fixture.Infrastructure.csproj` (refs Core) — `GreetRepository.cs`
  - `Fixture.Web/Fixture.Web.csproj` (refs Core + Infrastructure) — `Caller.cs` with a method calling `GreetUseCase.Greet` (for find_callers/find_usages)
- Create: `tests/CodeIntelligenceMcp.Tests/Integration/MsBuildFixture.cs` (collection fixture calling `RoslynLoader`'s MSBuild registration exactly once)
- Create: `tests/CodeIntelligenceMcp.Tests/Integration/RoslynIntegrationTests.cs`
- Modify: `.gitignore` if fixture `bin`/`obj` are not already covered (repo-wide `bin/`/`obj/` patterns probably cover it — verify)
- Do NOT add the fixture projects to `CodeIntelligenceMcp.slnx` — they are loaded only via MSBuildWorkspace at test time.

**Interfaces:**
- Consumes: `RoslynLoader.LoadAsync(path, cleanArch, ct)` — read its actual signature first and use it exactly.
- The fixture path is resolved relative to the test assembly: walk up from `AppContext.BaseDirectory` until a directory containing `tests/fixtures/FixtureSolution` is found.

- [ ] **Step 1: Create the fixture solution** (hand-write the .sln and csproj files — minimal SDK-style, 6 small .cs files). Verify it builds standalone: `dotnet build tests/fixtures/FixtureSolution -o <scratchpad>/fixturebuild`.

- [ ] **Step 2: Write the integration tests**

```csharp
[Collection("msbuild")]
public sealed class RoslynIntegrationTests(MsBuildFixture fixture)
{
    [Fact]
    public async Task LoadAsync_FixtureSolution_IndexesAllTypes()
    {
        RoslynWorkspaceIndex index = await fixture.GetIndexAsync();

        index.TypeCount.Should().BeGreaterThanOrEqualTo(5);
        index.GetType("GreetUseCase").Should().NotBeNull();
    }

    [Fact]
    public async Task FindImplementations_FixtureInterface_FindsConcreteType()
    {
        RoslynWorkspaceIndex index = await fixture.GetIndexAsync();

        var impls = index.FindImplementations("IGreetUseCase");

        impls.Should().ContainSingle(i => i.TypeName == "GreetUseCase");
    }

    [Fact]
    public async Task FindCallers_GreetMethod_FindsWebCaller()
    {
        RoslynWorkspaceIndex index = await fixture.GetIndexAsync();

        var callers = await new ReferenceQueries(index).FindCallersAsync("GreetUseCase", "Greet");

        callers.Should().Contain(c => c.CallerType == "Caller");
    }

    [Fact]
    public async Task DetectAsync_CoreNoHttp_FlagsHttpViolation()
    {
        RoslynWorkspaceIndex index = await fixture.GetIndexAsync();
        var detector = new ViolationDetector(index, new CleanArchitectureNames("Fixture.Core", "Fixture.Infrastructure", "Fixture.Web"));

        var violations = await detector.DetectAsync("core-no-http", CancellationToken.None);

        violations.Should().Contain(v => v.FilePath.EndsWith("HttpViolation.cs"));
    }
}
```

`MsBuildFixture`: `[CollectionDefinition("msbuild")]`; caches one `Task<RoslynWorkspaceIndex>` (build once, share across tests); registers MSBuildLocator via the same code path `RoslynLoader` uses (it is idempotent-guarded there — reuse, don't duplicate). Mark the class with `IAsyncLifetime` for disposal.

- [ ] **Step 3: Run** — expect several minutes NOT: the fixture is tiny, load should be < 30 s. If `dotnet test` fails because MSBuildLocator conflicts with the test host's loaded MSBuild assemblies, the standard fix is setting `MSBuildSDKsPath`-free child behavior — investigate with the actual error; a known-good fallback is running these tests serially in their own collection (already the case).

- [ ] **Step 4: Commit** — `test: fixture solution with end-to-end Roslyn integration tests`

### Task 19: Analyzer unit tests — ViolationDetector

**Files:**
- Create: `tests/CodeIntelligenceMcp.Tests/ViolationDetectorTests.cs`
- Consumes: `RoslynWorkspaceIndex.CreateForTesting` (existing) and `ViolationDetector.DetectAsync(rule, ct)`.

Cover at least these rules, one focused test each, via in-memory compilations with project names `App.Core` / `App.Infrastructure` / `App.Web` and `new CleanArchitectureNames("App.Core", "App.Infrastructure", "App.Web")`:
`core-no-http` (Core type using `System.Net.Http`), `missing-cancellation-token` (async method without CT), `no-async-void` (`async void` method), `empty-catch` (`catch { }`), `throw-ex` (`catch (Exception ex) { throw ex; }`), `too-many-params` (method with 6+ params), `dto-in-core` (type named `XxxDto` in Core project), `usecase-not-sealed` (unsealed `XxxUseCase`). Read `ViolationDetector.cs` first — if a rule needs Solution/documents (not available via `CreateForTesting`), move that rule's test to the integration fixture instead and note it in the test file header comment.

- [ ] **Step 1: Write the 8 tests (AAA, one rule per test).** Example shape:

```csharp
[Fact]
public async Task DetectAsync_EmptyCatch_FlagsMethod()
{
    RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
        "public class Svc { public void Run() { try { } catch { } } }"));
    var detector = new ViolationDetector(index, TestIndex.CleanArch);

    var violations = await detector.DetectAsync("empty-catch", CancellationToken.None);

    violations.Should().ContainSingle();
}
```

Create a small static `TestIndex` helper in the test project wrapping `CreateForTesting` with `(projectName, source)` tuples (reference implementation is in `RoslynLookupTests.cs` — extract/share it rather than duplicating).

- [ ] **Step 2: Run — fix any test misunderstanding of rule semantics by reading the detector, not by weakening assertions. Step 3: Commit** — `test: ViolationDetector rule coverage`

### Task 20: Analyzer unit tests — Complexity, Coupling, Risk, PatternScanner

**Files:**
- Create: `tests/CodeIntelligenceMcp.Tests/ComplexityAnalyzerTests.cs`, `CouplingAnalyzerTests.cs`, `RiskAnalyzerTests.cs`, `PatternScannerTests.cs`

Minimum assertions (read each analyzer first; use `TestIndex` from Task 19):
- Complexity: a method with 3 `if`s + 1 loop scores complexity 5±1 and a straight-line method scores 1; `minComplexity` filters.
- Coupling: a type referencing 6 distinct external types reports coupling >= 6; `minCoupling` filters.
- Risk: a type with high coupling + no matching `*Tests` class scores higher than the same type with a `FooTests` type present in a `.Tests` project.
- PatternScanner: counts use cases (`*UseCase` naming) and interfaces correctly on a 4-type compilation.

- [ ] **Step 1: Write tests. Step 2: Run green. Step 3: Commit** — `test: analyzer coverage for complexity, coupling, risk, patterns`

## Phase 5 — Docs & polish

### Task 21: Documentation truth pass

**Files:**
- Modify: `README.md` — add: copy-example-config step BEFORE the publish step; `--config`/`CODEINTEL_CONFIG` discovery; `claude mcp add` command and project-`.mcp.json` example; platform statement (tested on Windows; macOS/Linux via `dotnet publish -r <rid>` — MSBuildLocator needs the .NET SDK); `cleanArchitecture` semantics (drives layer rules; auto-detected from `*.Core`/`*.Infrastructure` naming when omitted); note that SSE mode is local-dev only (unauthenticated OAuth stub).
- Modify: `docs/TOOLS.md` — regenerate the full tool table from the `[McpServerTool]` attributes (all ~50 tools incl. `list_workspaces`, `find_derived_types`, new params `maxResults`/`depth`, the `OkList` envelope, wildcard syntax); replace personal `datalake2`/`C:/Git/...` examples with `myapp`/`C:/path/to/...`.
- Modify: `src/CodeIntelligenceMcp/Tools/CodebaseWikiTool.cs:12` and `DiagnosticsTool.cs:16` — replace `Datalake2.Core` example text in descriptions with `MyApp.Core`.
- Modify: `CLAUDE.md` — correct package versions (ModelContextProtocol 1.1.0, Roslyn 5.3.0), document the new envelope/`ToolResponses`/`WorkspaceAccess` conventions, remove the personal workspace table (point to mcp-config.example.json instead).
- Modify: `mcp-config.example.json` — add `python` and `javascript` workspace examples.

- [ ] **Step 1: Make all doc edits.** For TOOLS.md, grep all `[McpServerTool(Name = ...)]` + `[Description(...)]` and write the table from source — no stale hand-copied text.
- [ ] **Step 2: Build + full test run one last time. Step 3: Commit** — `docs: truthful README, regenerated TOOLS.md, scrubbed examples`

---

## Deliberately NOT in this plan (parked, from the audit)

- LICENSE choice, `dotnet tool` packaging, CI pipeline — blocked on the user's license/name decision.
- Disk cache / incremental indexing (R5/R6-L) — validate cold-start pain with real users first.
- Telemetry, open-core split, token benchmark (Z1–Z3) — need external users to matter.
- Rename impact analysis (C2) — feature roadmap, not a fix.

## Self-review notes

- Task ordering: 1 → 2 → 3/4/5/6 depend on 1 (and 2 for prologues). Phase 2+ tasks are independent of each other except Task 18 (integration) which should run after 7/11/12/13/14/15 land so it tests final behavior.
- `CreateForTesting` limitation (no Solution) is respected: SymbolFinder-dependent behavior (Tasks 12, parts of 19) is tested via the Task 18 fixture.
- Envelope consistency: every list tool goes through `OkList`; no tool builds its own JSON after Task 1 except `AmbiguityError` (which uses shared options).
