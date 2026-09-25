# CodeIntelligenceMcp — Tool Reference

CodeIntelligenceMcp is a .NET MCP server (stdio transport) that gives structured,
token-efficient access to indexed codebases instead of reading files directly.
Analysis tools are **read-only**. `save_workspace` is the explicit exception: it persists
runtime workspace registrations to the startup `mcp-config.json`. Workspaces are **lazy-loaded**: the first tool call
against a workspace triggers indexing; later calls reuse the in-memory index
until the workspace changes or `refresh_workspace` is called.

## The `workspace` parameter

Every tool takes a `workspace` argument. Three forms are accepted:

- **A name from `mcp-config.json`** — e.g. `"myapp"`. Use `list_workspaces` to see what is
  configured, including type and whether it is currently loaded.
- **A runtime name from `register_workspace`**. Runtime workspaces are available
  immediately to existing tools and appear in `list_workspaces` with source `runtime`.
- **An absolute path**, for ad-hoc use against a workspace that isn't pre-configured:
  - C#/.NET tools: an absolute path to a `.sln`, `.slnx`, or `.slnf` file, e.g.
    `C:/path/to/MyApp.sln`.
  - Classic ASP, JavaScript/TypeScript, Python, and PowerShell tools: an absolute path to
    the workspace root directory.

Ad-hoc paths work for lookup and search, but Clean Architecture rules that depend on named
projects (`core-no-ef`, etc.) only fire for workspaces declared in `mcp-config.json`, since
there is no project-name mapping for an unregistered path.

If a workspace name or path cannot be resolved, tools return an error object with a `hint`
listing the known workspaces of that type (see Response Envelope below) — never a silent
fallback or a stack trace.

---

## Response Envelope

All JSON-returning tools share one of three shapes. Wiki tools (`get_codebase_wiki`,
`get_js_wiki`, `get_python_wiki`, `get_powershell_wiki`) are the exception — they return
**Markdown**, not JSON.

### List tools

Tools that return a collection wrap it in an envelope with truncation metadata:

```json
{
  "total": 143,
  "returned": 100,
  "truncated": true,
  "stale": true,
  "hint": "truncated — refine the query or raise maxResults",
  "items": [ ... ]
}
```

- `total` — total matches found before truncation.
- `returned` — number of items actually included in `items`.
- `truncated` — `true` when `total` exceeds `returned`.
- `maxResults` (input parameter) defaults to `100`; pass `0` for unlimited.
- `stale` and `hint` are omitted (not just `false`/`null`) unless relevant. `stale: true`
  means source files changed since the index was built — call `refresh_workspace`.
  `hint` explains the `truncated` or `stale` condition; a truncation hint takes priority
  when both apply.

### Single-object tools

Tools that return one object (e.g. `get_type`, `get_dependencies`) return the object
directly:

```json
{ "name": "MyType", "kind": "class", "members": [ ... ] }
```

If the underlying index is stale, the object is wrapped instead:

```json
{ "stale": true, "hint": "index may be outdated — call refresh_workspace to rebuild", "result": { ... } }
```

### Errors

Any failure — workspace not found, symbol not found, invalid rule name, ambiguous type
name — returns:

```json
{ "error": "type not found", "hint": "known dotnet workspaces: myapp — or pass an absolute path", "detail": null }
```

`hint` and `detail` are omitted when not applicable. Type-name lookups that match more than
one fully-qualified type return an `ambiguous type name` error with all candidates in
`detail` instead of guessing.

### Wildcard search syntax

Search and "find" tools that accept a `query`, `nameContains`, `functionName`, or
`className`-style parameter support two matching modes on the same argument:

- If the value contains `*` or `?`, it is treated as a **glob** matched against the full
  symbol/function/class name (`*` = any run of characters, `?` = any single character).
- Otherwise it is a **case-insensitive substring** match.

Example: `find_types nameContains="*Request"` matches every type whose name ends in
`Request`; `find_types nameContains="request"` matches any type with `request` anywhere in
its name, case-insensitively.

---

## C# / .NET Tools (Roslyn-backed)

Source: `src/CodeIntelligenceMcp/Tools/CSharpTools.cs`. All require a `dotnet` workspace
(name from `mcp-config.json`, or an absolute `.sln`/`.slnx`/`.slnf` path).

| Tool | Purpose | Parameters |
|---|---|---|
| `get_type` | Full structural details of a type: properties, methods, base type, interfaces, attributes. | `workspace`; `typeName` — simple or fully qualified type name |
| `find_types` | Search for types by name, namespace, interface, attribute, or kind. | `workspace`; `nameContains` (default none) — substring or glob; `namespace` (default none) — exact or prefix match; `implementsInterface` (default none); `hasAttribute` (default none); `kind` (default none) — class/interface/record/enum; `maxResults` (default 100, 0 = unlimited) |
| `get_method` | Full source body of a specific method, without opening the file. | `workspace`; `typeName`; `methodName` |
| `find_implementations` | All concrete types implementing a given interface. | `workspace`; `interfaceName` — simple name or with type args, e.g. `IUseCase<CreateRequest, Result>`; `maxResults` (default 100, 0 = unlimited) |
| `find_derived_types` | All types deriving from a given base class, at any depth (complements `find_implementations` for interfaces). | `workspace`; `baseTypeName`; `maxResults` (default 100, 0 = unlimited) |
| `find_usages` | All usages of a type, method, or field across the workspace. | `workspace`; `symbolName` — `Type`, `Type.Member`, or a unique bare member name; ambiguous or unknown names return an error with candidates; `maxResults` (default 100, 0 = unlimited) |
| `get_dependencies` | Constructor-injected dependencies of a type. | `workspace`; `typeName` |
| `get_public_surface` | All public types in a namespace: interfaces, classes, records, enums. | `workspace`; `namespace` — exact or prefix match |
| `get_project_dependencies` | Project dependency graph — which projects reference which. | `workspace` |
| `search_symbol` | Substring/glob search across all symbol names (types, methods, properties); compiler-generated members excluded. | `workspace`; `query`; `maxResults` (default 100, 0 = unlimited) |
| `scan_patterns` | Count types, interfaces, use cases, Razor components; run all violation rules. Quick structural health check. | `workspace` |
| `get_test_coverage` | Which use cases have a matching `*Tests` class in any `.Tests` project (convention-based). Returns coverage percentage and uncovered use cases. | `workspace` |
| `get_complexity` | Methods ordered by cyclomatic complexity or line count. | `workspace`; `minComplexity` (default 5); `projectFilter` (default none) — substring match; `minLines` (default 0 = disabled); `sortBy` (default `"complexity"`, or `"lines"`); `maxResults` (default 100, 0 = unlimited) |
| `scan_all_violations` | Run every violation rule in one call. Returns `{ rules, skippedRules? }`: rules with violations ordered by count descending, plus any rule that failed to run (so an empty result is never a silent failure). | `workspace`; `maxPerRule` (default 50, 0 = unlimited) |
| `find_dead_code` | Private methods, properties, and fields with no references. | `workspace`; `projectFilter` (default none) — substring match; `maxResults` (default 100, 0 = unlimited) |
| `find_callers` | Callers of a method, optionally transitive (callers-of-callers). Returns caller type, method, file, line, calling line text, and depth. | `workspace`; `typeName` — owning type; `methodName`; `maxResults` (default 100, 0 = unlimited); `depth` (default 1, clamped 1-3) — 1 = direct callers only |
| `get_coupling` | Types ordered by efferent coupling (unique external types depended on). | `workspace`; `minCoupling` (default 5); `projectFilter` (default none) — substring match; `maxResults` (default 100, 0 = unlimited) |
| `get_hotspots` | Top N types by combined risk score (coupling + complexity + missing tests) — no type name needed. | `workspace`; `topN` (default 20); `projectFilter` (default none) — substring match |
| `find_circular_dependencies` | Cycles in the project dependency graph, each returned as an ordered list of project names. | `workspace` |
| `get_change_risk` | Refactoring risk score (0-100) for a type, based on referencing types, coupling, max complexity, and test coverage. | `workspace`; `typeName` |
| `find_violations` | Run one specific architectural rule across the workspace. See rule table below. | `workspace`; `rule`; `projectFilter` (default none) — substring match on file path; `maxResults` (default 100, 0 = unlimited) |
| `analyze_file` | Structural observations for a single `.cs`/`.razor` file: missing `CancellationToken`, layer violations, inline types, JSON in view. | `workspace`; `filePath` — relative to solution root, or absolute |

### `find_violations` rule keys

`core-no-ef`, `core-no-http`, `core-no-azure`, `usecase-not-sealed`, `inline-viewmodel-razor`,
`business-logic-in-razor`, `json-parsing-in-view`, `blazor-injects-infra`,
`controller-not-thin`, `dto-in-core`, `missing-cancellation-token`, `no-async-void`,
`async-over-sync`, `use-case-not-thin`, `empty-catch`, `throw-ex`, `layer-boundary`,
`too-many-params`, `services-in-web`, `missing-interface`, `direct-instantiation`.

---

## Wiki, Diagnostics, and Change Analysis (Roslyn-backed)

Source: `src/CodeIntelligenceMcp/Tools/CodebaseWikiTool.cs`,
`ChangeAnalysisTool.cs`, `DiagnosticsTool.cs`.

| Tool | Purpose | Parameters |
|---|---|---|
| `get_codebase_wiki` | Compact hierarchical overview of a .NET codebase: project structure, architectural patterns, health summary (violations), optional metrics. Call first in any session. Returns Markdown, not JSON. | `workspace`; `focusArea` (default none) — namespace prefix, e.g. `MyApp.Core.Features.Orders`; `includePatterns` (default `true`); `includeViolations` (default `true`); `includeMetrics` (default `false`) |
| `analyze_changes` | Git diff analysis between HEAD and a base branch: changed files, affected types, public API signature changes, violations and diagnostics scoped to changed code. | `workspace`; `baseBranch` (default `"main"`) — branch or commit (sha, `HEAD~3`); `includeSignatures` (default `true`); `includeDiagnostics` (default `true`); `includeUncommitted` (default `false`) — also include staged + unstaged working-tree changes |
| `get_diagnostics` | Roslyn compiler diagnostics (CS/IDE/CA/SA codes), grouped by diagnostic ID. Does not duplicate `find_violations` architectural rules. | `workspace`; `severity` (default `"warning"`) — `"error"` \| `"warning"` \| `"info"`; `project` (default none) — exact project name; `category` (default none) — code prefix filter |

---

## Classic ASP Tools

Source: `src/CodeIntelligenceMcp/Tools/AspClassicTools.cs`. Require an `asp-classic`
workspace (name from `mcp-config.json`, or an absolute path to the ASP root directory).

| Tool | Purpose | Parameters |
|---|---|---|
| `asp_get_file` | Full structure of a Classic ASP file: includes, subs, functions, variables, VBScript blocks. | `workspace`; `filePath` |
| `asp_find_symbol` | Find subs, functions, variables, or call sites by name (whole-word match) across all ASP files. | `workspace`; `symbolName`; `maxResults` (default 100, 0 = unlimited) |
| `asp_get_includes` | Include chain for an ASP file: direct includes and transitive includes with depth. | `workspace`; `filePath` |
| `asp_search` | Case-insensitive substring search across VBScript content in all ASP files. | `workspace`; `query`; `maxResults` (default 100, 0 = unlimited) |

---

## SQL Tools (Classic ASP workspaces)

Source: `src/CodeIntelligenceMcp/Tools/SqlTools.cs`. Same `asp-classic` workspace
requirement as the Classic ASP tools above; these do not accept `maxResults` (unbounded).

| Tool | Purpose | Parameters |
|---|---|---|
| `sql_find_table` | All SQL queries referencing a given table: operation type, signature, columns. | `workspace`; `tableName` |
| `sql_get_signatures` | All normalised SQL query signatures from a single ASP file. | `workspace`; `filePath` |
| `sql_find_column` | All SQL queries referencing a given column. | `workspace`; `columnName` |
| `sql_list_tables` | All tables in the workspace, sorted by usage count, with per-file usage. | `workspace` |

---

## JavaScript / TypeScript Tools

Source: `src/CodeIntelligenceMcp/Tools/JsTools.cs`. Require a `javascript` workspace
(name from `mcp-config.json`, or an absolute path to the project root).

| Tool | Purpose | Parameters |
|---|---|---|
| `get_js_wiki` | Compact overview of a JS/TS project: modules, components, exports, imports, dependencies, framework patterns (Vue SFC, Nuxt, React). Returns Markdown. | `workspace`; `focusArea` (default none) — subdirectory, e.g. `src/components`; `includePatterns` (default `true`); `includeMetrics` (default `false`) |
| `js_get_file` | Full analysis of a single JS/TS/Vue file: functions, classes, imports, exports, interfaces, or Vue SFC blocks. | `workspace`; `filePath` |
| `js_find_function` | Find functions by name (substring or glob), including inside Vue SFC script blocks. | `workspace`; `functionName`; `maxResults` (default 100, 0 = unlimited) |
| `js_find_class` | Find classes by name (substring or glob). | `workspace`; `className`; `maxResults` (default 100, 0 = unlimited) |
| `js_search` | Search across function names, class names, exports, and import paths. | `workspace`; `query`; `maxResults` (default 100, 0 = unlimited) |

---

## Python Tools

Source: `src/CodeIntelligenceMcp/Tools/PythonTools.cs`. Require a `python` workspace
(name from `mcp-config.json`, or an absolute path to the project root).

| Tool | Purpose | Parameters |
|---|---|---|
| `get_python_wiki` | Compact overview of a Python project: modules, classes, functions, imports, dependencies, framework patterns (async, Pydantic, FastAPI). Returns Markdown. | `workspace`; `focusArea` (default none) — subdirectory or module prefix, e.g. `src/api`; `includePatterns` (default `true`); `includeMetrics` (default `false`) |
| `py_get_file` | Full analysis of a single Python file: functions, classes, imports, exports. | `workspace`; `filePath` |
| `py_find_function` | Find functions and methods by name (substring or glob). | `workspace`; `functionName`; `maxResults` (default 100, 0 = unlimited) |
| `py_find_class` | Find classes by name (substring or glob). | `workspace`; `className`; `maxResults` (default 100, 0 = unlimited) |
| `py_search` | Search across function names, class names, and import paths. | `workspace`; `query`; `maxResults` (default 100, 0 = unlimited) |

---

## PowerShell Tools

Source: `src/CodeIntelligenceMcp/Tools/PowerShellTools.cs`. Require a `powershell`
workspace (name from `mcp-config.json`, or an absolute path to the project root).

| Tool | Purpose | Parameters |
|---|---|---|
| `get_powershell_wiki` | Compact overview of a PowerShell project: script structure, functions, module manifests, dependencies, patterns. Returns Markdown. | `workspace`; `focusArea` (default none) — subdirectory, e.g. `Deploy`; `includePatterns` (default `true`); `includeMetrics` (default `false`) |
| `ps_get_file` | Full analysis of a single script or module file: functions, imports, variables, cmdlet usage. | `workspace`; `filePath` |
| `ps_find_function` | Find functions by name (substring or glob) across all scripts. | `workspace`; `functionName`; `maxResults` (default 100, 0 = unlimited) |
| `ps_get_modules` | All module manifests (`.psd1`) with exported functions and dependencies. | `workspace` |
| `ps_search` | Search across function names, parameter names, and variables. | `workspace`; `query`; `maxResults` (default 100, 0 = unlimited) |

---

## Workspace Management

Source: `src/CodeIntelligenceMcp/Tools/WorkspaceManagementTool.cs`. No workspace-type
restriction — these operate across all configured workspaces.

| Tool | Purpose | Parameters |
|---|---|---|
| `list_workspaces` | List all configured and runtime workspaces with type, path, source, and whether each is currently indexed (loaded). | none |
| `register_workspace` | Register a workspace for this MCP server process only. Existing tools can use the new short name immediately. | `name`; `type`; `path`; optional `coreProject`, `infraProject`, `webProject` |
| `unregister_workspace` | Remove a runtime workspace registration and invalidate any cached index for that name. | `name` |
| `save_workspace` | Persist a runtime workspace to the `mcp-config.json` file used at startup. | `name` |
| `refresh_workspace` | Invalidate the in-memory index for a workspace so the next tool call re-indexes from scratch. Use after large file changes or branch switches. | `workspace` — name from `mcp-config.json`, or absolute path |

---

## Tool Use Priority

Start every session on a known .NET codebase with:

1. `get_codebase_wiki` — project structure, patterns, health summary.
2. `analyze_changes` — if working on a feature branch, understand what changed vs the base branch.
3. Drill down with the specific lookup/search tools above.

Prefer these tools over reading files directly whenever the workspace is indexed.
