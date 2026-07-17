# Runtime Workspace Registration Design

Date: 2026-07-17

## Goal

Make CodeIntelligenceMcp easier to use as a personal Codex/Claude tool by allowing an agent to register the current codebase during a session, give it a short name, and immediately run the existing code-intelligence tools against it.

The same product direction applies to SqlSchemaMcp, but database registration has a different security profile because it handles connection strings. CodeIntelligenceMcp is implemented first; SqlSchemaMcp follows with stricter credential handling.

## Non-Goals

- No automatic filesystem scanning for all repositories on disk.
- No automatic persistence. Runtime registration is temporary unless the user explicitly saves it.
- No mutation of source code or analyzed workspaces.
- No hidden storage of database credentials in tool responses or logs.
- No shared network service assumptions. Stdio remains the primary local-agent path.

## User Flows

### Temporary Code Workspace

1. The agent detects or is given a solution/root path.
2. The agent calls `register_workspace` with a short name such as `current`.
3. The workspace appears in `list_workspaces` with `source: "runtime"`.
4. Existing tools can use the short name:
   - `get_codebase_wiki("current")`
   - `analyze_changes("current")`
   - `get_hotspots("current")`
5. The registration disappears when the MCP process exits.

### Persisted Code Workspace

1. The user registers a runtime workspace.
2. The user explicitly calls `save_workspace(name)`.
3. The server appends that workspace to `mcp-config.json`.
4. Future sessions load it as a configured workspace.

### Temporary Database

1. The user gives SqlSchemaMcp a database key, engine, and connection string.
2. The server registers it for the current process only.
3. Discovery tools show the database key and engine, but never echo the connection string.
4. Existing schema tools can use the database key.

## CodeIntelligenceMcp Design

### Runtime Registry

Add a singleton `RuntimeWorkspaceRegistry` in the server project.

Responsibilities:

- Store `WorkspaceConfig` entries added after startup.
- Enforce case-insensitive unique names across configured and runtime workspaces.
- Return a merged view of configured and runtime workspaces for lookup and listing.
- Track each workspace source as `configured` or `runtime`.

The registry does not index anything itself. Existing workspace providers still own indexing and cache lifetime.

### Tool Surface

Add tools to `WorkspaceManagementTool`:

- `register_workspace(name, type, path, cleanArchitecture?)`
- `unregister_workspace(name)`
- `save_workspace(name)`

Update:

- `list_workspaces` includes `source`.
- `refresh_workspace` works for runtime names.

Validation:

- `name` is required, trimmed, and cannot contain path separators.
- `type` must be one of `dotnet`, `asp-classic`, `powershell`, `python`, `javascript`.
- `dotnet` path must exist and end with `.sln`, `.slnx`, or `.slnf`.
- File-walk workspace paths must be existing directories.
- A runtime name cannot overwrite a configured workspace.
- A duplicate runtime name is rejected. A future `replace: true` option is out of scope for v1.

### Provider Resolution

Change workspace resolution to use a workspace catalog instead of only `McpConfig.Workspaces`.

Resolution order:

1. Configured/runtime workspace by name and type.
2. Absolute path matching a configured/runtime workspace path.
3. Absolute ad-hoc path using the existing `CreateAdHoc` path.

Cache keys:

- Named configured/runtime workspaces use the workspace name.
- Ad-hoc absolute paths continue using the normalized path as the name.

### Persistence

`save_workspace(name)` persists one runtime workspace to `mcp-config.json`.

Rules:

- Save is explicit; `register_workspace` never writes files.
- The config path used at startup is kept in a small `McpConfigSource` singleton so tools know what file to update.
- Writes preserve only the known `McpConfig` schema, not comments.
- Save refuses to overwrite an existing configured name.
- Save returns a short JSON result with `saved: true`, `configPath`, and the saved workspace name.

This is acceptable because CodeIntelligenceMcp config contains local filesystem paths, not secrets.

## SqlSchemaMcp Design

SqlSchemaMcp follows the same shape, but starts with runtime-only database registration.

Proposed tools:

- `register_database(name, engine, connectionString)`
- `unregister_database(name)`
- Future option, not part of the first SqlSchemaMcp pass: `save_database(name)` after a separate security review.

Rules:

- `list_configured_databases` must mask whether the value came from config or runtime without exposing connection strings.
- Runtime registration must avoid logging connection strings.
- Connection validation should reuse the existing engine resolver and read-only startup gate logic.
- Persistent save is not included in the first SqlSchemaMcp pass because connection strings are credentials.

## Error Handling

All new CodeIntelligenceMcp tools use the existing `ToolResponses` envelope:

- Validation failures return `ToolResponses.Err`.
- Successful register/unregister/save operations return `ToolResponses.Ok`.
- `OperationCanceledException` is not swallowed.

Provider lookup failures continue through `WorkspaceAccess`.

## Testing

CodeIntelligenceMcp tests:

- Register a dotnet workspace and verify `list_workspaces` shows `source: "runtime"`.
- Registered dotnet workspace can be used by an existing tool via short name.
- Register rejects unknown type.
- Register rejects missing path.
- Register rejects duplicate configured name.
- Unregister removes runtime workspace and invalidates any loaded index.
- Save writes one runtime workspace to a temp config file.

SqlSchemaMcp tests for the separate SqlSchemaMcp implementation pass:

- Register runtime database without exposing connection string in list output.
- Unknown runtime database can be unregistered cleanly.
- Runtime database routes through existing engine/capability lookup.

## Rollout

1. Implement CodeIntelligenceMcp runtime registration.
2. Add docs showing how Codex/Claude can register the current solution at session start.
3. Implement CodeIntelligenceMcp `save_workspace`.
4. Design and implement SqlSchemaMcp runtime database registration.
5. Defer SqlSchemaMcp persistence until credential-handling rules are approved.

## V1 Decisions

- `register_workspace` does not support `replace: true` in v1.
- `save_workspace` rewrites the known JSON schema and does not preserve comments.
- SqlSchemaMcp does not persist connection strings in its first runtime-registration pass.
