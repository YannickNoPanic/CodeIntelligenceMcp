# CodeIntelligenceMcp — Tool Reference

All tools are read-only. Workspace names come from `mcp-config.json`.

### Worktree support

The `workspace` parameter on all tools accepts either:
- A **name** from `mcp-config.json` (e.g. `"datalake2"`)
- An **absolute path** to a `.sln` or `.slnx` file (e.g. `"C:/Git/Datalake2-feature/Datalake2.sln"`)

Use the path form when analyzing a git worktree that isn't pre-configured. Clean Architecture violation rules that require project names (core-no-ef, etc.) will not fire for ad-hoc paths since there is no config to map project names.

---

## Tool Use Priority

Start every session with this order:

1. **`get_codebase_wiki`** — understand what's there and what's wrong
2. **`analyze_changes`** — if on a branch, understand what changed vs main
3. Drill down with specific tools as needed

Do not read files directly when a tool can answer the question.

---

## Overview Tools

### `get_codebase_wiki`

**Purpose**: Single-call codebase overview. Project structure, architectural patterns, and health summary (violations). The entry point for any new session.

**When to use**: First call in any session touching a .NET workspace. Also use with `focusArea` when starting work on a specific domain.

**When NOT to use**: When you already have a current wiki from this session.

**Parameters**:
- `workspace` — workspace name (e.g. `"datalake2"`) or absolute path to a `.sln`/`.slnx` file
- `focusArea` — namespace prefix to scope output (e.g. `"Datalake2.Core.Features.Devices"`)
- `includePatterns` — include use cases, repositories, vertical slices (default `true`)
- `includeViolations` — include architectural violations health section (default `true`)
- `includeMetrics` — include type/file counts (default `false`)

**Output**: Markdown with sections: Project Structure, Architectural Patterns, Health Summary.

**Companion tools**: `find_violations` for the full violations list, `get_diagnostics` for compiler warnings.

---

### `analyze_changes`

**Purpose**: Git-aware analysis of current branch vs base branch. Returns changed files, affected types, public API signature changes, violations scoped to changed code, and diagnostics in changed files.

**When to use**: When on a feature branch, before merge, or after a refactor. Replaces reading git diffs and changed files manually.

**Parameters**:
- `workspace` — workspace name
- `baseBranch` — branch to compare against (default `"main"`)
- `includeSignatures` — detect public API signature changes (default `true`)
- `includeDiagnostics` — include Roslyn diagnostics scoped to changed files (default `true`)

**Output**:
```json
{
  "summary": { "changedFiles": 12, "affectedDomains": ["Devices"], "violationsCount": 2, ... },
  "changedFiles": [{ "filePath": "...", "status": "modified", "affectedTypes": ["DeviceMonitoringQuery"] }],
  "signatureChanges": [...],
  "violationsInChanges": [...],
  "diagnosticsInChanges": [...],
  "newTypes": [...]
}
```

**Companion tools**: `find_violations` for full workspace violations, `get_diagnostics` for all diagnostics.

---

## C# / .NET Tools (Roslyn-backed)

### `get_type`

**Purpose**: Full structural details of one type: properties, methods, base type, interfaces, attributes.

**When to use**: When you know the type name and need its members. More efficient than reading the file.

**Parameters**: `workspace`, `typeName` (simple or FQN)

---

### `find_types`

**Purpose**: Discover types by name substring, namespace, implemented interface, attribute, or kind.

**When to use**: When you don't know the exact type name, or want all types in a domain/namespace.

**Example**: `find_types workspace="datalake2" namespace="Datalake2.Core.Features.Devices"` lists everything in the Devices domain.

---

### `get_method`

**Purpose**: Full source body of a specific method without reading the file.

**Parameters**: `workspace`, `typeName`, `methodName`

---

### `find_implementations`

**Purpose**: All concrete types implementing a given interface.

**When to use**: To map an interface to its implementations, understand injection candidates.

---

### `find_usages`

**Purpose**: All usages of a symbol (type, method, field) across the workspace.

**When to use**: Before refactoring, to understand blast radius of a change.

---

### `get_dependencies`

**Purpose**: Constructor-injected dependencies of a type.

**When to use**: To understand what a class needs without reading it.

---

### `get_public_surface`

**Purpose**: All public types in a namespace: interfaces, classes, records, enums.

**When to use**: To understand what a layer exposes as its contract.

---

### `get_project_dependencies`

**Purpose**: Project dependency graph — which projects reference which.

**When to use**: To verify Clean Architecture layering or understand build order.

---

### `search_symbol`

**Purpose**: Substring search across all symbol names (types, methods, properties).

**When to use**: When you know part of a name but not the full path.

---

### `scan_patterns`

**Purpose**: Counts types, interfaces, use cases, razor components, and runs all violation rules in one call.

**When to use**: Quick structural health check when `get_codebase_wiki` is too broad.

---

### `find_violations`

**Purpose**: Run one specific architectural rule across the full workspace.

**Supported rules**:
| Rule | Detects |
|---|---|
| `core-no-ef` | EF Core references in Core project |
| `core-no-http` | HTTP client references in Core project |
| `core-no-azure` | Azure SDK references in Core project |
| `usecase-not-sealed` | Non-sealed IUseCase implementations |
| `inline-viewmodel-razor` | Private class inside Razor @code block |
| `business-logic-in-razor` | Use case calls or heavy LINQ in Razor |
| `json-parsing-in-view` | JsonDocument/JsonSerializer in Razor |
| `controller-not-thin` | Controller action with >10 lines of logic |
| `dto-in-core` | Types with Dto suffix in Core project |

---

### `analyze_file`

**Purpose**: Structural observations for a single .cs or .razor file: missing CancellationToken, layer violations, inline types, JSON in view.

**When to use**: After `scan_patterns` or `find_violations` identifies an issue file, to get line-level detail.

**Parameters**: `workspace`, `filePath` (relative to solution root or absolute)

---

### `get_diagnostics`

**Purpose**: Roslyn compiler diagnostics (CS/IDE/CA codes) grouped by diagnostic ID. Covers compiler output — does not duplicate `find_violations` architectural rules.

**When to use**: After a refactor to check for new warnings. Start filtered to one project and `severity=warning` to avoid noise.

**Parameters**:
- `workspace`
- `severity` — `"error"` | `"warning"` | `"info"` (default `"warning"`)
- `project` — filter to one project by exact name
- `category` — filter by code prefix: `"CS"`, `"IDE"`, `"CA"`, `"SA"`

**Output**:
```json
{
  "totalDiagnostics": 187,
  "groups": [
    { "id": "CS8600", "severity": "warning", "count": 23, "examples": [...] }
  ]
}
```

---

## Classic ASP Tools

### `asp_get_file`

**Purpose**: Full structure of an ASP file: includes, subs, functions, variables, VBScript blocks.

**When to use**: Instead of reading the .asp file directly.

---

### `asp_find_symbol`

**Purpose**: Find subs, functions, variables, or call sites by name across all ASP files.

---

### `asp_get_includes`

**Purpose**: Include chain for an ASP file: direct and transitive includes with depth.

---

### `asp_search`

**Purpose**: Substring search across VBScript content in all ASP files.

---

## SQL Tools (Classic ASP workspaces)

### `sql_find_table`

**Purpose**: All SQL queries referencing a given table, with operation type and columns.

---

### `sql_get_signatures`

**Purpose**: All normalised SQL query signatures from a single ASP file.

---

### `sql_find_column`

**Purpose**: All queries referencing a given column.

---

### `sql_list_tables`

**Purpose**: All tables in the workspace sorted by usage count.

**When to use**: To understand data access patterns across the full ASP codebase.

---

## PowerShell Tools

### `get_powershell_wiki`

**Purpose**: Overview of a PowerShell workspace: scripts, functions, modules, patterns.

---

### `ps_get_file`

**Purpose**: Full analysis of one PowerShell script: functions, imports, variables, cmdlet usage.

---

### `ps_find_function`

**Purpose**: Find functions by name across all scripts.

---

### `ps_get_modules`

**Purpose**: All module manifests (.psd1) with exported functions and dependencies.

---

### `ps_search`

**Purpose**: Substring search across function names, parameters, and variables.
