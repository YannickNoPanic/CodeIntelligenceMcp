# Implementation Plan

## How to use this file
At the start of each session: read this file, find the first unchecked phase, execute it top-to-bottom.
Mark each item `[x]` as it is completed. Each phase ends with a passing `dotnet build`.

---

## Phase 1 — Solution scaffold
- [x] Create directory structure (`src/`, `tests/`, subdirs)
- [x] Copy `vbscript-parser/VBScript.Parser/` → `src/VBScript.Parser/`
- [x] Retarget `VBScript.Parser` to `net10.0`, fix `Range` ambiguity (CS0104)
- [x] Write `src/VBScript.Parser/CHANGES.md`
- [x] Create `Directory.Build.props` (net10.0, nullable, implicit usings)
- [x] Create `CodeIntelligenceMcp.sln` with all 5 projects
- [x] Create `src/CodeIntelligenceMcp.AspClassic/CodeIntelligenceMcp.AspClassic.csproj`
- [x] Create `src/CodeIntelligenceMcp.Roslyn/CodeIntelligenceMcp.Roslyn.csproj` (Roslyn + MSBuild.Locator packages)
- [x] Create `src/CodeIntelligenceMcp/CodeIntelligenceMcp.csproj` (ModelContextProtocol 1.2.0 + Hosting)
- [x] Create `tests/CodeIntelligenceMcp.Tests/CodeIntelligenceMcp.Tests.csproj` (xUnit, FluentAssertions, NSubstitute)
- [x] Create `.gitignore` (bin/, obj/, mcp-config.local.json)
- [x] Stub `Program.cs` (just enough to compile)
- [x] `dotnet build` green

---

## Phase 2 — Config loading
Files to create:
- `src/CodeIntelligenceMcp/Config/McpConfig.cs` — model records
- `src/CodeIntelligenceMcp/Config/McpConfigLoader.cs` — loads mcp-config.json (no variable substitution needed, paths are absolute)

Tasks:
- [x] Define `McpConfig`, `WorkspaceConfig`, `CleanArchitectureConfig` records
- [x] `McpConfigLoader.Load(string configPath)` reads JSON, deserializes, returns `McpConfig`
- [x] Fail at startup with clear message if a workspace path does not exist
- [x] `dotnet build` green

---

## Phase 3 — Roslyn indexer
Files to create in `src/CodeIntelligenceMcp.Roslyn/`:
- `Models/TypeInfo.cs`
- `Models/MethodInfo.cs`
- `Models/ProjectDependency.cs`
- `Models/PatternSummary.cs`
- `Models/ViolationResult.cs`
- `RoslynLoader.cs` — opens MSBuildWorkspace, compiles solution
- `RoslynWorkspaceIndex.cs` — holds compiled symbols, exposes query methods
- `BlazorFilePreprocessor.cs` — extracts @code block from .razor for C# analysis
- `PatternScanner.cs` — backs `scan_patterns` tool
- `ViolationDetector.cs` — all 9 violation rules

Tasks:
- [x] Define all 5 model records
- [x] `RoslynLoader`: `MSBuildLocator.RegisterDefaults()`, open workspace, compile solution
- [x] `RoslynWorkspaceIndex`: index all named types with file/line, expose `GetType`, `FindTypes`, `FindImplementations`, `FindUsages`, `GetPublicSurface`, `SearchSymbol`, `GetProjectDependencies`
- [x] `BlazorFilePreprocessor`: extract `@code { }` block with line offset preserved
- [x] `ViolationDetector`: implement all 9 rules (pure Roslyn, no regex on source)
  - `core-no-ef`, `core-no-http`, `core-no-azure`
  - `usecase-not-sealed`
  - `inline-viewmodel-razor`, `business-logic-in-razor`, `json-parsing-in-view`
  - `controller-not-thin`
  - `dto-in-core`
- [x] `PatternScanner`: count types, interfaces, use cases, razor components; run all violation rules; emit observations
- [x] `dotnet build` green

---

## Phase 4 — ASP Classic indexer
Files to create in `src/CodeIntelligenceMcp.AspClassic/`:
- `Models/AspFileInfo.cs`
- `Models/VbscriptBlock.cs`
- `Models/SqlQueryInfo.cs`
- `AspBlockExtractor.cs` — character-by-character, line tracking, emits VbscriptBlock list
- `SqlExtractor.cs` — detects SQL assignments, follows `&_` continuations, normalises
- `VbscriptParserAdapter.cs` — wraps VBScript.Parser, fallback to regex on parse failure
- `AspIndex.cs` — walks .asp files, builds full index

Tasks:
- [x] Define 3 model records
- [x] `AspBlockExtractor`: walk raw file, track line numbers, emit `<% %>` blocks with `lineStart`/`lineEnd`
- [x] `SqlExtractor`: detect SQL assignments, follow `&_` continuations, replace `& var &` with `{var}`, extract tables/columns
- [x] `VbscriptParserAdapter`: call `VBScriptParser`, extract subs/functions/variables; fallback regex if parser throws
- [x] `AspIndex`: walk all `.asp` files in rootPath, build index of all files
- [x] `dotnet build` green

---

## Phase 5 — MCP Tools + Program.cs
Files to create in `src/CodeIntelligenceMcp/Tools/`:
- `CSharpTools.cs` — wires all Roslyn index methods to MCP tool handlers
- `AspClassicTools.cs` — wires ASP index methods to MCP tool handlers
- `SqlTools.cs` — wires SQL query methods to MCP tool handlers

Files to replace:
- `src/CodeIntelligenceMcp/Program.cs` — full startup: load config, build indexes, register tools, stdio transport

Tasks:
- [x] `CSharpTools`: implement `get_type`, `find_types`, `get_method`, `find_implementations`, `find_usages`, `get_dependencies`, `get_public_surface`, `get_project_dependencies`, `search_symbol`, `scan_patterns`, `find_violations`, `analyze_file`
- [x] `AspClassicTools`: implement `asp_get_file`, `asp_find_symbol`, `asp_get_includes`, `asp_search`
- [x] `SqlTools`: implement `sql_find_table`, `sql_get_signatures`, `sql_find_column`, `sql_list_tables`
- [x] `Program.cs`: load config → build Roslyn index → build ASP index → register all tools → start stdio transport
- [x] `dotnet run` starts without errors

---

## Phase 6 — Tests
Files to create in `tests/CodeIntelligenceMcp.Tests/`:
- `SqlExtractorTests.cs`
- `AspBlockExtractorTests.cs`
- `ViolationDetectorTests.cs`
- `PatternScannerTests.cs`
- `RoslynLookupTests.cs`

Tasks:
- [x] `SqlExtractorTests`: multi-line concatenation, `{variable}` substitution, table/column extraction
- [x] `AspBlockExtractorTests`: line number preservation, `<%= %>` handling, nested HTML
- [x] `RoslynLookupTests`: `FindTypes`, `FindImplementations`, `FindUsages` against in-memory compilation
- [x] `dotnet test` green (44/44)
