# CodeIntelligenceMcp — Project Context

## What this project is

A .NET 10 MCP server (stdio transport) that gives Claude Code structured,
token-efficient access to codebases without Claude needing to read files directly.
Supports five workspace types: dotnet (Roslyn), asp-classic, powershell, python, javascript.

Workspaces are **lazy-loaded**: the server starts instantly and indexes on the first tool call
per workspace. Subsequent calls are instant. All tools are **read-only**.
No write operations, no file watchers, no hot reload.

---

## Commands

```bash
dotnet build src/CodeIntelligenceMcp                         # build the server
dotnet test tests/CodeIntelligenceMcp.Tests                  # run all tests (unit + integration)
dotnet run --project src/CodeIntelligenceMcp --no-launch-profile -c Release --no-build  # run server (stdio)
```

Build/test outputs may be locked by live MCP server processes. Never kill those
processes — build to an isolated output dir instead (`-o <tempdir>`).

---

## Reference docs

- **docs/TOOLS.md** — per-tool reference (regenerated from the tool attributes)
- **docs/handoff/CONVENTIONS.md** — mandatory implementation patterns (response
  pipeline, error boundary, path relativization, matchers)
- **docs/product-audit-2026-07-06.md** — the audit that drove the current shape
- **TASK.md / PLAN.md** — original build spec and history (partially stale; do not
  treat as current contracts)

---

## Solution structure

```
src/
  CodeIntelligenceMcp/          # MCP server entry point (Exe)
  CodeIntelligenceMcp.Common/   # Shared utilities (SourceFileWalker, NameMatcher) for the file-walk indexers
  CodeIntelligenceMcp.Roslyn/   # C# + Blazor indexer (Roslyn + MSBuild.Locator)
  CodeIntelligenceMcp.AspClassic/  # Classic ASP + SQL indexer
  CodeIntelligenceMcp.JavaScript/  # JS/TS/Vue indexer (line-based)
  CodeIntelligenceMcp.Python/   # Python indexer (line-based)
  CodeIntelligenceMcp.PowerShell/  # PowerShell indexer
  VBScript.Parser/              # Forked from YannickNoPanic/vbscript-parser, owned source
tests/
  CodeIntelligenceMcp.Tests/    # xUnit + FluentAssertions (unit + Integration/ against the fixture)
  fixtures/FixtureSolution/     # 3-project solution loaded via real MSBuildWorkspace in integration tests
```

---

## Workspace configuration

Workspaces are defined in `mcp-config.json` — **gitignored**, copy
`mcp-config.example.json` to get started. Paths are absolute, no variable
substitution. Config discovery order: `--config` CLI arg, `CODEINTEL_CONFIG`
env var, file next to the binary. Missing config is non-fatal (ad-hoc absolute
`.sln`/`.slnx`/`.slnf` paths work on every dotnet tool).

---

## Server conventions (enforced — see docs/handoff/CONVENTIONS.md for detail)

- All tool responses go through `Tools/ToolResponses.cs`: `Ok`, `OkList`
  (envelope `{ total, returned, truncated, stale?, hint?, items }`), `Err`.
  Never introduce a second serializer or envelope style.
- All workspace access goes through `Workspaces/WorkspaceAccess.GetAsync` —
  it converts every failure into short, actionable error JSON. Indexer load
  failures throw `WorkspaceLoadException(message, hint, detail)`.
- Response file paths are workspace-relative (forward slashes) via
  `RoslynWorkspaceIndex.Rel()`; internal storage stays absolute.
- Name matching: `SymbolQueryMatcher` (Roslyn) / `NameMatcher` (Common) —
  deliberately duplicated because `.Roslyn` must not depend on `.Common`.

---

## Key technical decisions

### VBScript.Parser
Copied from `vbscript-parser/VBScript.Parser/` into `src/VBScript.Parser/`.
Changes made (documented in `src/VBScript.Parser/CHANGES.md`):
- Retargeted from `netstandard2.0` to `net10.0`
- Fixed `Range` ambiguity (CS0104): `new Range(...)` → `new Ast.Range(...)` in `VBScriptParser.cs`

### Package versions (as pinned in the csproj files)
`ModelContextProtocol` 1.1.0 (stdio transport), `Microsoft.CodeAnalysis.*` 5.3.0,
`Microsoft.Build.Locator` 1.11.2, `LibGit2Sharp` 0.31.0, `Tomlyn` 0.17.0.
`RoslynLoader.RegisterMSBuild()` guards `MSBuildLocator.RegisterDefaults()` —
always call through it, never register directly.

---

## Architecture constraints for this project

- Tools in `CodeIntelligenceMcp/Tools/` are thin: delegate to index classes, no logic
- All indexing logic lives in the language indexer projects (`.Roslyn`, `.AspClassic`, `.JavaScript`, `.Python`, `.PowerShell`)
- Language indexer projects have no dependency on each other; the file-walk indexers may depend on `CodeIntelligenceMcp.Common`
- `CodeIntelligenceMcp.Common` and `VBScript.Parser` have no dependencies beyond the framework
- No business logic in `Program.cs` — only wiring

---

## What deviates from global CLAUDE.md

- No `Result<T>` for Roslyn index methods that cannot fail at the call site —
  use direct return types there; `Result<T>` applies to config loading and file operations
- No EF Core anywhere in this project
- No DI extension methods per domain — `Program.cs` registers everything directly
  (project is small enough that `AddCore()` / `AddInfrastructure()` would be over-engineering)
- No NSubstitute in tests — fakes are small inline classes
