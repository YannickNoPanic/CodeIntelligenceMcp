# CodeIntelligenceMcp

A .NET 10 MCP server that gives Claude Code structured, token-efficient access to your codebases without Claude needing to read files directly.

Supports five workspace types: **.NET/C#** (Roslyn), **Classic ASP/VBScript**, **PowerShell**, **Python**, and **JavaScript/TypeScript**.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio or MSBuild installed (required for .NET workspace loading)

---

## Setup

**1. Clone and build**

```bash
git clone https://github.com/YannickNoPanic/CodeIntelligenceMcp.git
cd CodeIntelligenceMcp
dotnet build src/CodeIntelligenceMcp -c Release
```

**2. Configure workspaces**

Edit `mcp-config.json` in the repo root with your absolute paths:

```json
{
  "workspaces": [
    {
      "name": "my-app",
      "type": "dotnet",
      "solution": "C:/path/to/MyApp.sln",
      "cleanArchitecture": {
        "coreProject": "MyApp.Core",
        "infraProject": "MyApp.Infrastructure",
        "webProject": "MyApp"
      }
    },
    {
      "name": "my-scripts",
      "type": "powershell",
      "rootPath": "C:/path/to/PowerShellScripts"
    },
    {
      "name": "my-classic-app",
      "type": "asp-classic",
      "rootPath": "C:/path/to/ClassicAspApp"
    },
    {
      "name": "my-python",
      "type": "python",
      "rootPath": "C:/path/to/PythonProject"
    },
    {
      "name": "my-frontend",
      "type": "javascript",
      "rootPath": "C:/path/to/JsProject"
    }
  ]
}
```

`cleanArchitecture` is optional — omit it if your .NET workspace does not follow Clean Architecture.

**3. Register with Claude Code (stdio — recommended)**

Add to `~/.claude/settings.json` (global, works across all projects):

```json
{
  "mcpServers": {
    "code-intelligence": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/path/to/CodeIntelligenceMcp/src/CodeIntelligenceMcp",
        "--no-launch-profile",
        "-c",
        "Release",
        "--no-build"
      ]
    }
  }
}
```

Claude Code spawns a fresh server process per session. No separate process to manage.

**3b. SSE mode (optional)**

Start the server manually:

```bash
dotnet run --project src/CodeIntelligenceMcp --no-launch-profile -c Release -- --sse
```

Then connect via URL in `~/.claude/settings.json`:

```json
{
  "mcpServers": {
    "code-intelligence": {
      "type": "http",
      "url": "http://localhost:5100/"
    }
  }
}
```

The default port is `5100`. Override via `appsettings.json`:

```json
{
  "Mcp": { "Port": 5200 }
}
```

---

## Available tools

### All workspace types

| Tool | Description |
|---|---|
| `get_codebase_wiki` | Architecture overview, violations, hotspots — call this first each session |
| `refresh_workspace` | Clear cached index and force reload on next tool call |

### .NET / C# (dotnet workspaces)

| Tool | Description |
|---|---|
| `search_symbol` | Fuzzy search across all symbols |
| `find_types` | Search types by name, namespace, interface, attribute, or kind |
| `get_type` | Full type info: members, base types, attributes, file location |
| `get_method` | Method signature, parameters, return type, body |
| `find_implementations` | All implementations of an interface |
| `find_usages` | All usages of a type across the codebase |
| `find_callers` | All callers of a method |
| `get_public_surface` | Public API of a namespace or project |
| `get_dependencies` | Dependencies of a type or namespace |
| `get_coupling` | Coupling metrics between modules |
| `get_project_dependencies` | Full project dependency graph |
| `find_violations` | Detect a specific Clean Architecture rule violation |
| `scan_all_violations` | Run all violation rules and return a summary |
| `scan_patterns` | Architecture pattern overview (use cases, repos, controllers) |
| `get_complexity` | Cyclomatic complexity per method or file |
| `find_dead_code` | Unreachable or unused code |
| `get_hotspots` | Files with high complexity — biggest refactor candidates |
| `get_change_risk` | Risk score for files on the current branch |
| `analyze_changes` | Summary of what changed on the current branch |
| `analyze_file` | Full analysis of a single file |
| `get_diagnostics` | Roslyn compiler warnings and errors |
| `get_test_coverage` | Test project coverage overview |
| `find_circular_dependencies` | Detect circular dependencies between projects |

### PowerShell (powershell workspaces)

| Tool | Description |
|---|---|
| `get_powershell_wiki` | Compact overview: scripts, functions, modules, patterns |
| `ps_get_file` | Full analysis of a single script or module |
| `ps_find_function` | Find functions by name across all scripts |
| `ps_get_modules` | List all module manifests and their exports |
| `ps_search` | Search across function names, parameters, and variables |

### Classic ASP / VBScript (asp-classic workspaces)

| Tool | Description |
|---|---|
| `asp_get_file` | Full analysis of a single .asp file |
| `asp_find_symbol` | Find a sub, function, or variable by name |
| `asp_get_includes` | Resolve `#include` chain for a file |
| `asp_search` | Search across all ASP files |

### SQL (asp-classic workspaces)

| Tool | Description |
|---|---|
| `sql_find_table` | Find all queries referencing a table |
| `sql_get_signatures` | SQL query signatures from a file |
| `sql_find_column` | Find queries referencing a specific column |
| `sql_list_tables` | List all tables referenced in the workspace |

### Python (python workspaces)

| Tool | Description |
|---|---|
| `get_python_wiki` | Overview of modules, classes, and functions |
| `py_get_file` | Full analysis of a single Python file |
| `py_find_class` | Find classes by name |
| `py_find_function` | Find functions by name |
| `py_search` | Search across all Python files |

### JavaScript / TypeScript (javascript workspaces)

| Tool | Description |
|---|---|
| `get_js_wiki` | Overview of modules, classes, and functions |
| `js_get_file` | Full analysis of a single JS/TS file |
| `js_find_class` | Find classes by name |
| `js_find_function` | Find functions by name |
| `js_search` | Search across all JS/TS files |

---

## Notes

- All tools are read-only — the server never modifies files
- Workspaces are **lazy-loaded**: indexed on the first tool call per session, not at startup
- Each stdio session starts a fresh server with its own in-memory cache; use `refresh_workspace` to reload within a session
- `mcp-config.json` uses absolute paths — no environment variable substitution

---

## Troubleshooting

**Claude can't connect / MCP fails to start**

Check the log file at `%TEMP%\CodeIntelligenceMcp.log`. Every session writes a `--- Session started ---` header followed by startup context. If the file is missing or has no new entry, the process never ran — verify `dotnet` is on PATH and the build output exists at `bin/Release/net10.0/`.

**Workspace fails to load**

The log will contain an `[ERR]` entry with the exception. Common causes: `.sln` file not found, `rootPath` directory missing, or MSBuild not installed. Fix the path in `mcp-config.json` and restart the session.

**Multiple Claude sessions in the same folder**

Supported — each session spawns its own server process and all write to the same shared log file.

**Port already in use (SSE mode)**

Change the port in `appsettings.json` under `Mcp:Port`, then update the URL in your settings.

**Stdio mode: garbled output**

A dependency may be writing to stdout. Check with `dotnet run 2>/dev/null`. All internal logging goes to the log file and stderr.
