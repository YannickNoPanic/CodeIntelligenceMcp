# ARCHITECTURE.md — Python & JavaScript Integration Architecture

## Current Architecture

CodeIntelligenceMcp uses a consistent per-language pattern. Each language is fully independent:

```
src/
  CodeIntelligenceMcp/                     # Entry point (Exe)
    Program.cs                             # Startup: load config, build indexes, wire DI + tools
    Config/                                # McpConfig, McpConfigLoader
    Tools/
      CSharpTools.cs                       # Roslyn-backed MCP tools
      AspClassicTools.cs                   # ASP Classic MCP tools
      SqlTools.cs                          # SQL MCP tools
      CodebaseWikiTool.cs                  # get_codebase_wiki (C#/.NET)
      PowerShellTools.cs                   # PowerShell MCP tools

  CodeIntelligenceMcp.Roslyn/             # C#/.NET indexer (MSBuild + Roslyn)
  CodeIntelligenceMcp.AspClassic/         # ASP Classic + VBScript indexer
  CodeIntelligenceMcp.PowerShell/         # PowerShell script indexer
  VBScript.Parser/                        # VBScript AST parser (owned source)

tests/
  CodeIntelligenceMcp.Tests/
```

### Per-Language Pattern

Every language follows the same structure inside its project:

| Class | Responsibility |
|---|---|
| `{Lang}Index` | Static `Build(rootPath, log)` factory. Holds all parsed data in memory. Exposes query methods. |
| `{Lang}ScriptParser` or `{Lang}FileParser` | Parses a single file's content → model records |
| `{Lang}WikiGenerator` | Formats compact wiki text from an index |
| `{Lang}IndexRegistry` | `record` wrapping `IReadOnlyDictionary<string, {Lang}Index>` — used as DI singleton |
| `{Lang}Tools` | `[McpServerToolType]` class in main project. Thin: validate workspace, delegate to index/generator. |

### DI + Startup Pattern

`Program.cs` does all wiring directly (no `AddCore()`/`AddInfrastructure()` pattern — the project
is small enough that domain extension methods would be over-engineering):

```csharp
// 1. Load config
McpConfig config = McpConfigLoader.Load(configPath);

// 2. Build indexes per workspace type
foreach (WorkspaceConfig ws in config.Workspaces)
{
    if (ws.Type == "dotnet")          // → RoslynWorkspaceIndex
    if (ws.Type == "asp-classic")     // → AspIndex
    if (ws.Type == "powershell")      // → PowerShellIndex
    // ... new types added here
}

// 3. Register index registries as singletons
builder.Services.AddSingleton(new RoslynIndexRegistry(roslynIndexes));
builder.Services.AddSingleton(new AspIndexRegistry(aspIndexes));
builder.Services.AddSingleton(new PowerShellIndexRegistry(psIndexes));

// 4. Register tool types
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CSharpTools>()
    .WithTools<PowerShellTools>()
    // ... new tool types added here
```

### Registry Pattern

Each language has a registry record. Example:

```csharp
// In CodeIntelligenceMcp (main project)
public sealed record PowerShellIndexRegistry(IReadOnlyDictionary<string, PowerShellIndex> Indexes);
```

The tool class receives the registry via primary constructor:

```csharp
public sealed class PowerShellTools(PowerShellIndexRegistry indexes)
{
    [McpServerTool(Name = "get_powershell_wiki")]
    public string GetPowerShellWiki(string workspace, ...)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PowerShellIndex? index))
            return Err("workspace not found");
        ...
    }
}
```

---

## Proposed Changes

### New Projects

```
src/
  CodeIntelligenceMcp.Python/             # NEW — Python indexer
    Models/PythonFileInfo.cs
    PythonFileParser.cs
    PythonPackageParser.cs
    PythonIndex.cs
    PythonWikiGenerator.cs

  CodeIntelligenceMcp.JavaScript/         # NEW — JavaScript/TypeScript/Vue indexer
    Models/JsFileInfo.cs
    Models/VueSfcInfo.cs
    JsFileParser.cs
    VueSfcExtractor.cs
    JsPackageParser.cs
    JsIndex.cs
    JsWikiGenerator.cs
```

### New Tools

```
src/CodeIntelligenceMcp/Tools/
  PythonTools.cs                          # NEW — get_python_wiki, py_get_file, py_find_function, py_find_class, py_search
  JsTools.cs                              # NEW — get_js_wiki, js_get_file, js_find_function, js_find_class, js_search
```

### Program.cs Changes

Two new `else if` branches in the workspace loading loop:

```csharp
else if (ws.Type == "python" && ws.RootPath is not null)
{
    PythonIndex index = PythonIndex.Build(ws.RootPath, msg => Console.Error.WriteLine(msg));
    pyIndexes[ws.Name] = index;
}
else if (ws.Type == "javascript" && ws.RootPath is not null)
{
    JsIndex index = JsIndex.Build(ws.RootPath, msg => Console.Error.WriteLine(msg));
    jsIndexes[ws.Name] = index;
}
```

Two new singleton registrations (in both stdio and SSE branches):

```csharp
builder.Services.AddSingleton(new PythonIndexRegistry(pyIndexes));
builder.Services.AddSingleton(new JsIndexRegistry(jsIndexes));
```

Two new tool registrations:

```csharp
.WithTools<PythonTools>()
.WithTools<JsTools>()
```

### csproj References

`CodeIntelligenceMcp.csproj` gets two new project references:

```xml
<ProjectReference Include="..\CodeIntelligenceMcp.Python\CodeIntelligenceMcp.Python.csproj" />
<ProjectReference Include="..\CodeIntelligenceMcp.JavaScript\CodeIntelligenceMcp.JavaScript.csproj" />
```

---

## Architecture Decisions

### Decision 1: No Shared ICodebaseAnalyzer Interface

A forced common interface would not add value here. The existing language indexes have
fundamentally different APIs:

- `RoslynWorkspaceIndex`: type hierarchy, symbol resolution, violation detection, Blazor support
- `AspIndex`: SQL extraction, VBScript blocks, include resolution
- `PowerShellIndex`: module manifests, cmdlet usage tracking, pipeline support detection
- `PythonIndex` (proposed): package dependencies, framework detection, `__all__` exports
- `JsIndex` (proposed): Vue SFC components, Nuxt conventions, ESM/CJS detection

An interface thin enough to cover all of these would be just `Build()` + `GetFile()` — which
adds no value over the registry pattern already in place. An interface broad enough to be
useful would require every language to stub out methods that don't apply to it.

The registry pattern already provides all the DI abstraction needed. Keep the existing approach.

### Decision 2: Per-Language Tools (Not Unified)

The task doc asks whether to use a unified `get_codebase_wiki` or per-language tools.
The existing `CodebaseWikiTool.cs` already registers `get_codebase_wiki` for C#/.NET.
Overloading that tool with language detection would:
- Make the tool description ambiguous
- Require language detection at query time (when is it a Python workspace vs JS workspace?)
- Couple unrelated concerns in one class

Per-language tools (`get_python_wiki`, `get_js_wiki`) follow the PowerShell pattern and are
clearer for Claude Code to use — the tool name communicates the language explicitly.

### Decision 3: Custom Regex Parsers (No AST Libraries)

For Python and JavaScript/TypeScript there is no .NET-native AST parser with acceptable
trade-offs (see `DEPENDENCIES.md` for full comparison). The wiki use case requires only
structural extraction (names, line ranges, imports, exports), not semantic analysis.
Custom regex parsers are:
- Zero external dependencies
- Consistent with `VbscriptParserAdapter` (regex fallback pattern already in this repo)
- Fast (no compilation step)
- Maintainable (in-repo, no third-party version drift)

### Decision 4: Tomlyn for TOML (Python Only)

`pyproject.toml` uses TOML's nested table feature (`[project.optional-dependencies]`,
`[tool.poetry.dependencies]`). Regex on nested TOML tables is unreliable. `Tomlyn` is a
pure .NET TOML v1.0 parser, MIT license, no native dependencies. The single exception to
the "no NuGet" rule for Python is justified here because the alternative (brittle regex) is worse.

### Decision 5: VueSfcExtractor as Separate Class

The Vue SFC block extraction (`<template>`, `<script>`, `<style>`) is structurally identical
to `AspBlockExtractor`'s `<% %>` extraction — both are character-by-character scans with line
tracking. Keeping it as a separate class:
- Mirrors the existing pattern
- Is independently testable
- Keeps `JsFileParser` focused on parsing JS/TS syntax

---

## Full Project Structure After Changes

```
src/
  CodeIntelligenceMcp/
    Program.cs                             # +2 workspace branches, +2 registries, +2 tool types
    Config/
      McpConfig.cs
      McpConfigLoader.cs
    Tools/
      CSharpTools.cs
      AspClassicTools.cs
      SqlTools.cs
      CodebaseWikiTool.cs
      PowerShellTools.cs
      PythonTools.cs                       # NEW
      JsTools.cs                           # NEW

  CodeIntelligenceMcp.Roslyn/
  CodeIntelligenceMcp.AspClassic/
  CodeIntelligenceMcp.PowerShell/
  VBScript.Parser/

  CodeIntelligenceMcp.Python/              # NEW
    Models/
      PythonFileInfo.cs
    PythonFileParser.cs
    PythonPackageParser.cs
    PythonIndex.cs
    PythonWikiGenerator.cs

  CodeIntelligenceMcp.JavaScript/          # NEW
    Models/
      JsFileInfo.cs
      VueSfcInfo.cs
    JsFileParser.cs
    VueSfcExtractor.cs
    JsPackageParser.cs
    JsIndex.cs
    JsWikiGenerator.cs

tests/
  CodeIntelligenceMcp.Tests/
    PythonFileParserTests.cs               # NEW
    PythonPackageParserTests.cs            # NEW
    JsFileParserTests.cs                   # NEW
    VueSfcExtractorTests.cs                # NEW
```

---

## mcp-config.json Schema Additions

Two new workspace types. No schema changes required beyond adding new `type` values:

```json
{
  "workspaces": [
    {
      "name": "datalake2",
      "type": "dotnet",
      "solution": "C:/Git/Datalake2/Datalake2.sln",
      "cleanArchitecture": { ... }
    },
    {
      "name": "my-fastapi-backend",
      "type": "python",
      "rootPath": "C:/Projects/Backend"
    },
    {
      "name": "my-nuxt-frontend",
      "type": "javascript",
      "rootPath": "C:/Projects/Frontend"
    }
  ]
}
```

---

## Breaking Changes

None. All changes are purely additive:
- New projects do not affect existing projects
- New tool types do not conflict with existing tool names
- New workspace types are ignored by existing type branches in Program.cs
- New config properties are optional
