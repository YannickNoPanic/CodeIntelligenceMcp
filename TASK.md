# TASK: CodeIntelligenceMcp

## Goal

Build a .NET 10 MCP server (stdio transport) that gives Claude Code structured,
token-efficient access to two codebases:

- **Datalake2** — .NET 10 Blazor Server, Clean Architecture (Core / Infrastructure / Web)
- **Datalake1** — Classic ASP (VBScript + inline SQL)

The server indexes both on startup (in-memory, always fresh) and exposes MCP tools
that Claude Code can call instead of reading files directly.

All tools are read-only. The user decides what to do with the information — this server
only finds and surfaces structure. Claude reasons about it.

---

## Definition of Done

- [ ] `dotnet run` starts the MCP server on stdio without errors
- [ ] All tools listed below return correct structured output for both workspaces
- [ ] `mcp-config.json` drives all paths — no hardcoded paths in code
- [ ] Roslyn workspace loads Datalake2.sln including project references
- [ ] ASP indexer correctly extracts VBScript blocks from mixed HTML/.asp files
- [ ] SQL extractor normalises multi-line concatenated strings into single signatures
- [ ] `analyze_file` returns structured observations without Claude needing to read the file
- [ ] All tools follow the response contract (see below)
- [ ] Unit tests cover: SQL extractor, ASP block extractor, violation detection, Roslyn symbol lookup
- [ ] `scan_patterns` returns correct counts and observations across the full workspace
- [ ] `mcp-config.local.json` variable substitution works, gitignored file is excluded
- [ ] No hardcoded strings, magic numbers, or TODO comments

---

## Solution Structure

```
CodeIntelligenceMcp/
├── src/
│   ├── CodeIntelligenceMcp/                  # MCP server entry point
│   │   ├── CodeIntelligenceMcp.csproj
│   │   ├── Program.cs
│   │   ├── mcp-config.json                   # committed, uses ${GIT_ROOT} variable
│   │   ├── mcp-config.local.json             # gitignored, defines GIT_ROOT per machine
│   │   └── Tools/
│   │       ├── CSharpTools.cs
│   │       ├── AspClassicTools.cs
│   │       └── SqlTools.cs
│   ├── CodeIntelligenceMcp.Roslyn/           # C# + Blazor indexer
│   │   ├── CodeIntelligenceMcp.Roslyn.csproj
│   │   ├── RoslynWorkspaceIndex.cs
│   │   ├── RoslynLoader.cs
│   │   ├── BlazorFilePreprocessor.cs
│   │   ├── PatternScanner.cs                 # backs scan_patterns tool
│   │   ├── ViolationDetector.cs
│   │   └── Models/
│   │       ├── TypeInfo.cs
│   │       ├── MethodInfo.cs
│   │       ├── ProjectDependency.cs
│   │       ├── PatternSummary.cs
│   │       └── ViolationResult.cs
│   ├── CodeIntelligenceMcp.AspClassic/       # ASP + SQL indexer
│   │   ├── CodeIntelligenceMcp.AspClassic.csproj
│   │   ├── AspIndex.cs
│   │   ├── AspBlockExtractor.cs              # strips HTML, extracts <% %> with line map
│   │   ├── VbscriptParserAdapter.cs          # wraps VBScript.Parser project reference
│   │   ├── SqlExtractor.cs
│   │   └── Models/
│   │       ├── AspFileInfo.cs
│   │       ├── VbscriptBlock.cs
│   │       └── SqlQueryInfo.cs
│   └── VBScript.Parser/                      # forked from YannickNoPanic/vbscript-parser
│       └── VBScript.Parser.csproj            # owned, modifiable, no external dependency
├── tests/
│   └── CodeIntelligenceMcp.Tests/
│       ├── CodeIntelligenceMcp.Tests.csproj
│       ├── SqlExtractorTests.cs
│       ├── AspBlockExtractorTests.cs
│       ├── ViolationDetectorTests.cs
│       ├── PatternScannerTests.cs
│       └── RoslynLookupTests.cs
└── CodeIntelligenceMcp.sln
```

---

## Configuration

### mcp-config.json (committed to repo)

```json
{
  "variables": {},
  "workspaces": [
    {
      "name": "datalake2",
      "type": "dotnet",
      "solution": "${GIT_ROOT}/Datalake2/Datalake2.sln",
      "cleanArchitecture": {
        "coreProject": "Datalake2.Core",
        "infraProject": "Datalake2.Infrastructure",
        "webProject": "Datalake2.Web"
      }
    },
    {
      "name": "datalake1",
      "type": "asp-classic",
      "rootPath": "${GIT_ROOT}/Datalake1"
    }
  ]
}
```

### mcp-config.local.json (gitignored, per machine)

```json
{
  "variables": {
    "GIT_ROOT": "C:/Git"
  }
}
```

### Resolution rules

1. On startup, load `mcp-config.json` first
2. If `mcp-config.local.json` exists alongside it, merge — local `variables` override base
3. Resolve all `${VAR}` placeholders in paths using the merged variables map
4. If a `${VAR}` cannot be resolved, fail at startup with a clear message naming the variable
5. All paths are resolved relative to the config file location if not absolute after variable substitution

Add `mcp-config.local.json` to `.gitignore` in the generated solution.

---

## Dependencies

### CodeIntelligenceMcp (server)
- `ModelContextProtocol` — official Anthropic MCP SDK
- `Microsoft.Extensions.Hosting`

### CodeIntelligenceMcp.Roslyn
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `Microsoft.Build.Locator` — finds installed MSBuild/VS automatically

### CodeIntelligenceMcp.AspClassic
- Project reference to `VBScript.Parser` (see below) — no NuGet package

### VBScript.Parser (forked source, owned)

Clone from the fork and add as a project in the solution:

```
git clone https://github.com/YannickNoPanic/vbscript-parser.git extern/vbscript-parser
```

Copy `extern/vbscript-parser/VBScript.Parser/` into `src/VBScript.Parser/` and add to the
solution. Do not reference `extern/` directly — copy the source so it is fully owned.

Verify it compiles against .NET 10 before proceeding. If the project targets an older framework,
retarget to `net10.0` and resolve any breaking changes. Document any changes made in a
`CHANGES.md` inside `src/VBScript.Parser/`.

### Tests
- `xUnit`
- `FluentAssertions`
- `NSubstitute`

---

## MCP Tools — Full Specification

All tools return JSON. All errors return `{ "error": "message" }`.
Workspace parameter is always the workspace `name` from config (e.g. `"datalake2"`).

---

### C# Tools (Roslyn-backed)

#### `get_type`
```
Input:  workspace, typeName (simple or fully qualified)
Output: {
  name, namespace, kind,          // kind: class|interface|record|enum
  filePath, lineStart,
  baseType, interfaces[],
  attributes[],
  properties[]: { name, type, accessibility },
  methods[]:    { name, returnType, parameters[], accessibility, lineStart }
  constructorParameters[]: { name, type }
}
```

#### `find_types`
```
Input:  workspace, 
        filter: {
          nameContains?,        // substring match, case-insensitive
          namespace?,           // exact or prefix match
          implementsInterface?, // e.g. "IUseCase"
          hasAttribute?,        // e.g. "Authorize"
          kind?                 // class|interface|record|enum
        }
Output: [{ name, namespace, filePath, lineStart, kind }]
```

#### `get_method`
```
Input:  workspace, typeName, methodName
Output: {
  typeName, methodName, filePath, lineStart, lineEnd,
  signature,
  body                          // raw source of the method body
}
```

#### `find_implementations`
```
Input:  workspace, interfaceName
Output: [{ typeName, namespace, filePath, lineStart }]
```

#### `find_usages`
```
Input:  workspace, symbolName   // type name, method name, or field name
Output: [{ 
  filePath, lineNumber, 
  context,                      // the line of code where usage occurs
  usageKind                     // inheritance|injection|instantiation|call|reference
}]
```

#### `get_dependencies`
```
Input:  workspace, typeName
Output: {
  typeName,
  constructorParameters: [{ name, type }],
  injects: [{ name, type }]     // same list, named for clarity
}
```

#### `get_public_surface`
```
Input:  workspace, namespace    // exact or prefix
Output: {
  namespace,
  interfaces: [{ name, filePath }],
  publicClasses: [{ name, filePath }],
  publicRecords: [{ name, filePath }],
  enums: [{ name, filePath }]
}
```

#### `get_project_dependencies`
```
Input:  workspace
Output: {
  projects: [{ name, path }],
  dependencies: [{ from, to }]  // directed edges
}
```

#### `search_symbol`
```
Input:  workspace, query        // substring, case-insensitive
Output: [{ 
  symbolName, kind, typeName, namespace, filePath, lineNumber 
}]
```

#### `scan_patterns`
```
Input:  workspace
Output: {
  structural_summary: {
    total_types,
    interfaces,
    use_cases,
    razor_components,
    asp_files?,               // asp-classic workspaces only
    sql_queries?              // asp-classic workspaces only
  },
  observations: [
    // each entry is a factual count or distribution, no advice
    // examples:
    // "IUseCase implementations: 12 sealed, 2 not sealed"
    // "JsonDocument usage: 6 files — 3 in .razor components"
    // "inline viewmodel classes in .razor: 2"
    // "async methods missing CancellationToken: 4"
    // "controller actions with direct DbContext reference: 0"
    // "ASP files with SQL queries: 34, distinct tables referenced: 18"
  ]
}
```

`scan_patterns` runs all violation rules and observation heuristics across the whole
workspace and returns a compact summary. Use this as a first pass to know where to look,
then drill into specifics with `find_violations` or `analyze_file`.

---

#### `find_violations`
```
Input:  workspace, rule         // see rule list below
Output: [{ 
  rule, filePath, lineNumber, typeName?, methodName?,
  description                   // human-readable explanation of why this is a violation
}]
```

**Supported rules:**

| Rule key | What it detects |
|---|---|
| `core-no-ef` | Any `using Microsoft.EntityFrameworkCore` in the Core project |
| `core-no-http` | Any `using System.Net.Http` or `IHttpClientFactory` in Core |
| `core-no-azure` | Any Azure SDK namespace in Core |
| `usecase-not-sealed` | Classes implementing `IUseCase<,>` that are not `sealed` |
| `inline-viewmodel-razor` | `private class` defined inside a `.razor` @code block |
| `business-logic-in-razor` | `.razor` files that directly call a use case or contain LINQ projections over 10 lines |
| `json-parsing-in-view` | `JsonDocument` or `JsonSerializer` used in a `.razor` file |
| `controller-not-thin` | Controller action methods with more than 10 lines of logic |
| `dto-in-core` | Types with `Dto` suffix found in the Core project |

---

#### `analyze_file`
```
Input:  workspace, filePath     // relative to solution/root
Output: {
  filePath,
  fileType,                     // "razor" | "cs" | "asp"
  observations: [
    {
      kind,                     // see kind list below
      location,                 // "ClassName.MethodName" or line reference
      detail                    // specific description, no generic advice
    }
  ]
}
```

**Observation kinds (no thresholds — structural facts only):**

| Kind | Triggered when |
|---|---|
| `inline-type` | A `class` or `record` is defined inside a `.razor` @code block |
| `business-logic-in-view` | A use case is called directly in a `.razor` file |
| `json-parsing-in-view` | `JsonDocument`/`JsonSerializer` used in a `.razor` file |
| `data-assembly-in-component` | A method in a `.razor` component performs joins/projections (LINQ SelectMany, GroupBy, ToDictionary) |
| `direct-db-in-controller` | A controller action references a DbContext or repository directly |
| `missing-cancellation-token` | A `public async` method has no `CancellationToken` parameter |
| `layer-violation` | An import violates Clean Architecture layer rules |

`analyze_file` reports facts. It does not recommend actions. Claude decides what to do.

---

### Classic ASP Tools

#### `asp_get_file`
```
Input:  workspace, filePath
Output: {
  filePath,
  includes: [{ path, line }],
  subs: [{ name, parameters[], lineStart, lineEnd }],
  functions: [{ name, parameters[], lineStart, lineEnd }],
  variables: [{ name, line }],    // module-level Dim declarations
  vbscriptBlocks: [{              // each <% %> block extracted
    lineStart, lineEnd,
    source                        // raw VBScript, HTML stripped
  }]
}
```

#### `asp_find_symbol`
```
Input:  workspace, symbolName
Output: [{ filePath, lineNumber, kind, context }]
// kind: sub|function|variable|call
```

#### `asp_get_includes`
```
Input:  workspace, filePath
Output: {
  filePath,
  includes: [{ path, resolvedPath, line, exists }],
  transitiveIncludes: [{ path, resolvedPath, depth }]
}
```

#### `asp_search`
```
Input:  workspace, query        // substring, case-insensitive, searches VBScript content only
Output: [{ filePath, lineNumber, context }]
```

---

### SQL Tools (both workspaces)

#### `sql_find_table`
```
Input:  workspace, tableName
Output: [{ 
  filePath, lineStart,
  operation,                    // SELECT|INSERT|UPDATE|DELETE
  signature,                    // normalised query with {variable} placeholders
  columns: []                   // columns referenced, empty if SELECT *
}]
```

#### `sql_get_signatures`
```
Input:  workspace, filePath
Output: [{ 
  lineStart, lineEnd,
  operation,
  tables: [],
  columns: [],
  signature,
  parameters: []                // {variable} placeholders found in the query
}]
```

#### `sql_find_column`
```
Input:  workspace, columnName
Output: [{ filePath, lineNumber, tableName?, operation, signature }]
```

#### `sql_list_tables`
```
Input:  workspace
Output: [{ 
  tableName,
  usageCount,
  files: [{ filePath, usageCount }]
}]
// sorted by usageCount descending
```

---

## Key Implementation Notes

### Startup sequence

```
1. Load mcp-config.json
2. For each dotnet workspace:   load MSBuildWorkspace → compile → index all symbols
3. For each asp-classic workspace: walk .asp files → extract blocks → parse VBScript → extract SQL
4. Register MCP tools
5. Start stdio transport
```

Index is built once. No file watchers. Restart to refresh.

### ASP block extraction

`.asp` files mix HTML and `<% %>` VBScript blocks. The extractor must:

1. Walk the raw file character by character
2. Track line numbers throughout (do not lose position)
3. Emit each `<% %>` block as a `VbscriptBlock` with `lineStart`/`lineEnd` from the original file
4. Strip `=` from `<%= expr %>` output expressions — treat as VBScript expression
5. Pass extracted VBScript source to `VBScript.Parser` (forked, in `src/VBScript.Parser/`) for AST parsing via `VbscriptParserAdapter`
6. Fall back to regex-based sub/function detection if the AST parser throws (VBScript is forgiving, files may have syntax the parser does not handle)

### SQL extraction and normalisation

Queries in Classic ASP are often concatenated across lines:

```vbscript
tsql = "SELECT [id],[name] " &_
       "FROM [dbo].[View_PRTG] " &_
       "WHERE [Org] = '" & v_OrgName & "'"
```

The SQL extractor must:

1. Detect string variable assignments that start with `SELECT|INSERT|UPDATE|DELETE` (case-insensitive)
2. Follow `&_` continuation lines and join them into a single string
3. Replace `" & variableName & "` injections with `{variableName}` placeholders
4. Normalise whitespace (collapse multiple spaces/newlines to single space)
5. Extract table names from `FROM`, `JOIN`, `INTO`, `UPDATE` clauses (simple regex, not full SQL parser)
6. Extract column names from `SELECT` clause — stop at `FROM`, skip `*`
7. Store `lineStart` as the line of the assignment, `lineEnd` as the last continuation line

### Roslyn — Blazor / .razor support

Roslyn processes `.razor` files via the Razor compiler integration. When loading the workspace:

- Include `.razor` files in the compilation
- The `@code { }` block is treated as a partial class by the Razor compiler — Roslyn sees it as C#
- `@inject`, `@page`, `@using` directives are available via `RazorCodeDocument` if needed
- For `analyze_file` on `.razor` files: parse the file twice — once as HTML/Razor for structural observations (inject count, nested component depth), once via Roslyn for the @code block C# analysis

### ViolationDetector

Each rule in `find_violations` is a separate method on `ViolationDetector`. Rules are:

- Pure Roslyn queries — no regex on source text
- Run against the already-built compilation — no re-parsing
- Return `ViolationResult` records with file, line, type, and a specific description

Do not build a plugin system. A `switch` on rule key calling the appropriate method is sufficient.

---

## Code Style

Follow the global CLAUDE.md exactly:

- File-scoped namespaces
- Primary constructors where possible
- `Result<T>` for operations that can fail — never throw for expected failures  
- `CancellationToken` on all async methods
- No AutoMapper — explicit projection where needed
- `var` only when type is obvious from RHS
- No TODO comments — implement or ask
- No emojis anywhere

---

## Pre-Implementation Checklist

Before writing any code, confirm in this order:

- [ ] Clone `https://github.com/YannickNoPanic/vbscript-parser.git` and verify it builds
- [ ] Retarget `VBScript.Parser` to `net10.0` if needed — document any changes in `CHANGES.md`
- [ ] `Microsoft.Build.Locator` can locate MSBuild on the target machine (`MSBuildLocator.RegisterDefaults()` does not throw)
- [ ] `ModelContextProtocol` NuGet package resolves and supports stdio transport
- [ ] Resolve `${GIT_ROOT}` from `mcp-config.local.json` and verify `Datalake2.sln` exists at the resulting path
- [ ] Verify `C:/Git/Datalake1` exists and contains `.asp` files

If any item cannot be confirmed, stop and report — do not proceed with stubs or assumptions.

---

## Out of Scope

- File watchers or hot reload
- Write operations of any kind (this is read-only)
- JScript support in Classic ASP files
- `.inc` files (not needed per config decision)
- Standalone `.sql` files
- Any UI or web interface
- Authentication or authorisation on the MCP server itself
