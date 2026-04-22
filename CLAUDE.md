# CodeIntelligenceMcp — Project Context

## What this project is

A .NET 10 MCP server (stdio transport) that gives Claude Code structured,
token-efficient access to two codebases without Claude needing to read files directly.

Workspaces are **lazy-loaded**: the server starts instantly and indexes on the first tool call
per workspace. Subsequent calls are instant. All tools are **read-only**.
No write operations, no file watchers, no hot reload.

---

## Commands

```bash
dotnet build src/CodeIntelligenceMcp                         # build the server
dotnet test tests/CodeIntelligenceMcp.Tests                  # run unit tests
dotnet run --project src/CodeIntelligenceMcp --no-launch-profile -c Release --no-build  # run server (stdio)
```

---

## Reference docs

- **TASK.md** — full tool signatures and output contracts
- **PLAN.md** — implementation history (all phases complete)

---

## Solution structure

```
src/
  CodeIntelligenceMcp/          # MCP server entry point (Exe)
  CodeIntelligenceMcp.Roslyn/   # C# + Blazor indexer (Roslyn + MSBuild.Locator)
  CodeIntelligenceMcp.AspClassic/  # Classic ASP + SQL indexer
  VBScript.Parser/              # Forked from YannickNoPanic/vbscript-parser, owned source
tests/
  CodeIntelligenceMcp.Tests/    # xUnit + FluentAssertions + NSubstitute
```

---

## Target workspaces

Defined in `mcp-config.json` (root of repo). Paths are absolute — no variable substitution.

| Name | Type | Path |
|---|---|---|
| `datalake2` | `dotnet` | `C:/Git/Datalake2.0/Datalake2.sln` |
| `datalake1` | `asp-classic` | `C:/Git/WR_Development_datalake_portal` |

Clean Architecture projects for datalake2: `Datalake2.Core`, `Datalake2.Infrastructure`, `Datalake2` (web).

---

## Key technical decisions

### Config loading
`mcp-config.json` uses hardcoded absolute paths — the `${VAR}` substitution described in
TASK.md is **not needed**. `McpConfigLoader` just deserializes and validates paths exist.

### VBScript.Parser
Copied from `vbscript-parser/VBScript.Parser/` into `src/VBScript.Parser/`.
Changes made (documented in `src/VBScript.Parser/CHANGES.md`):
- Retargeted from `netstandard2.0` to `net10.0`
- Fixed `Range` ambiguity (CS0104): `new Range(...)` → `new Ast.Range(...)` in `VBScriptParser.cs`

### ModelContextProtocol
Resolved to **1.2.0** (latest stable at time of scaffold). Use stdio transport.

### Roslyn packages
`Microsoft.CodeAnalysis.CSharp.Workspaces` + `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.13.0,
`Microsoft.Build.Locator` 1.7.8. Call `MSBuildLocator.RegisterDefaults()` before opening workspace.

---

## Architecture constraints for this project

- Tools in `CodeIntelligenceMcp/Tools/` are thin: delegate to index classes, no logic
- All indexing logic lives in `CodeIntelligenceMcp.Roslyn` or `CodeIntelligenceMcp.AspClassic`
- `CodeIntelligenceMcp.Roslyn` and `CodeIntelligenceMcp.AspClassic` have no dependency on each other
- `VBScript.Parser` has no dependencies beyond the framework
- No business logic in `Program.cs` — only wiring

---

## What deviates from global CLAUDE.md

- No `Result<T>` for Roslyn index methods that cannot fail at the call site —
  use direct return types there; `Result<T>` applies to config loading and file operations
- No EF Core anywhere in this project
- No DI extension methods per domain — `Program.cs` registers everything directly
  (project is small enough that `AddCore()` / `AddInfrastructure()` would be over-engineering)
