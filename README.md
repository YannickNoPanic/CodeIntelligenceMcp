# CodeIntelligenceMcp

A .NET 10 MCP server (stdio transport) that gives Claude Code structured,
token-efficient access to your codebases without Claude needing to read files directly.

Supports three workspace types: **.NET/C#** (via Roslyn), **Classic ASP/VBScript**, and **PowerShell**.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio or MSBuild installed (required for Roslyn workspace loading)

---

## Setup

**1. Clone and build**

```bash
git clone https://github.com/<your-name>/CodeIntelligenceMcp.git
cd CodeIntelligenceMcp
dotnet build
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
    }
  ]
}
```

`cleanArchitecture` is optional — omit it if your .NET workspace is not Clean Architecture.

**3. Register with Claude Code (stdio — default)**

Add to `.claude/settings.json`:

```json
{
  "mcpServers": {
    "CodeIntelligenceMcp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/CodeIntelligenceMcp/src/CodeIntelligenceMcp", "--no-build"],
      "cwd": "C:/path/to/CodeIntelligenceMcp"
    }
  }
}
```

`dotnet run` (no arguments) starts in stdio mode. Claude Code connects directly — no separate server process needed.

**3b. SSE mode (optional, for power users)**

Start the server manually:

```bash
dotnet run --project src/CodeIntelligenceMcp -- --sse
```

Then connect via URL in `.claude/settings.json`:

```json
{
  "mcpServers": {
    "CodeIntelligenceMcp": {
      "url": "http://localhost:5100/sse"
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

### .NET / C# (dotnet workspaces)

| Tool | Description |
|---|---|
| `get_type` | Full type info: members, base types, attributes, file location |
| `find_types` | Search types by name, namespace, interface, attribute, or kind |
| `get_method` | Method signature, parameters, return type |
| `find_implementations` | Find all implementations of an interface |
| `find_usages` | Find all usages of a type across the codebase |
| `get_public_surface` | Public API of a type |
| `get_project_dependencies` | Project dependency graph |
| `search_symbol` | Fuzzy search across all symbols |
| `scan_patterns` | Overview of architecture patterns and metrics |
| `find_violations` | Detect Clean Architecture violations |
| `analyze_file` | Analyze a single file |

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

### SQL (available on asp-classic workspaces)

| Tool | Description |
|---|---|
| `sql_find_table` | Find all queries referencing a table |
| `sql_get_signatures` | Get SQL query signatures from a file |
| `sql_find_column` | Find queries referencing a specific column |
| `sql_list_tables` | List all tables referenced in the workspace |

---

## Notes

- All tools are read-only — the server never modifies files
- Workspaces are indexed once at startup (in-memory); restart the server to pick up changes
- `mcp-config.json` and `appsettings.json` are gitignored — they are machine-specific

---

## Troubleshooting

**Stdio mode produces no output / Claude can't connect**
Make sure nothing in the startup path writes to stdout. All internal logging goes to stderr.
If you see garbled output, a dependency may be writing to stdout — check with `dotnet run 2>/dev/null`.

**Port already in use (SSE mode)**
Change the port in `appsettings.json` under `Mcp:Port`, then update the URL in your `settings.json`.

**Workspace fails to load**
Check the path in `mcp-config.json`. For `dotnet` workspaces the `.sln` file must exist at the exact path.
For `asp-classic` and `powershell` workspaces the `rootPath` directory must exist.
Errors are logged to stderr with `[warn]` prefix — the server continues without the failed workspace.
