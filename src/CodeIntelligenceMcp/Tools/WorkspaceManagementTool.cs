namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class WorkspaceManagementTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslyn,
    IWorkspaceProvider<AspIndex> asp,
    IWorkspaceProvider<PowerShellIndex> ps,
    IWorkspaceProvider<PythonIndex> py,
    IWorkspaceProvider<JsIndex> js,
    WorkspaceCatalog catalog,
    McpConfigSource configSource)
{
    [McpServerTool(Name = "list_workspaces")]
    [Description("List all configured workspaces with type, path, and whether they are already indexed. Absolute .sln/.slnx paths also work ad hoc on any dotnet tool.")]
    public string ListWorkspaces()
    {
        var workspaces = catalog.List().Select(e => new
        {
            name = e.Workspace.Name,
            type = e.Workspace.Type,
            path = e.Path,
            source = e.Source,
            loaded = IsLoadedFor(e.Workspace)
        }).ToList();

        return ToolResponses.Ok(new { workspaces });
    }

    [McpServerTool(Name = "register_workspace")]
    [Description("Register a workspace for this MCP server process only. Use save_workspace to persist it.")]
    public string RegisterWorkspace(
        [Description("Short workspace name, for example 'current'")] string name,
        [Description("Workspace type: dotnet, asp-classic, powershell, python, or javascript")] string type,
        [Description("Solution path for dotnet, root directory for file-walk workspace types")] string path,
        [Description("Optional Clean Architecture core project name for dotnet workspaces")] string? coreProject = null,
        [Description("Optional Clean Architecture infrastructure project name for dotnet workspaces")] string? infraProject = null,
        [Description("Optional Clean Architecture web/API project name for dotnet workspaces")] string? webProject = null)
    {
        CleanArchitectureConfig? cleanArchitecture =
            string.IsNullOrWhiteSpace(coreProject)
            && string.IsNullOrWhiteSpace(infraProject)
            && string.IsNullOrWhiteSpace(webProject)
                ? null
                : new CleanArchitectureConfig
                {
                    CoreProject = coreProject ?? string.Empty,
                    InfraProject = infraProject ?? string.Empty,
                    WebProject = webProject ?? string.Empty
                };

        RegistrationResult result = catalog.Register(name, type, path, cleanArchitecture);
        return result.Success
            ? ToolResponses.Ok(new
            {
                registered = true,
                name = result.Workspace!.Name,
                type = result.Workspace.Type,
                path = result.Workspace.Solution ?? result.Workspace.RootPath,
                source = "runtime"
            })
            : ToolResponses.Err(result.Error!);
    }

    [McpServerTool(Name = "unregister_workspace")]
    [Description("Remove a runtime workspace registration from this MCP server process.")]
    public string UnregisterWorkspace(
        [Description("Runtime workspace name to remove")] string name)
    {
        if (!catalog.IsRuntime(name))
            return ToolResponses.Err($"workspace '{name}' is not a runtime workspace");

        bool removed = catalog.Unregister(name);
        bool invalidated = roslyn.Invalidate(name) | asp.Invalidate(name)
            | ps.Invalidate(name) | py.Invalidate(name) | js.Invalidate(name);

        return ToolResponses.Ok(new { name, unregistered = removed, invalidated });
    }

    [McpServerTool(Name = "save_workspace")]
    [Description("Persist a runtime workspace to the mcp-config.json file used at startup.")]
    public string SaveWorkspace(
        [Description("Runtime workspace name to persist")] string name)
    {
        WorkspaceConfig? runtime = catalog.GetRuntime(name);
        if (runtime is null)
            return ToolResponses.Err($"workspace '{name}' is not a runtime workspace");

        McpConfig existing = McpConfigLoader.Load(configSource.Path);
        if (existing.Workspaces.Any(w => string.Equals(w.Name, runtime.Name, StringComparison.OrdinalIgnoreCase)))
            return ToolResponses.Err($"configured workspace '{runtime.Name}' already exists in '{configSource.Path}'");

        existing.Workspaces.Add(runtime);

        string json = JsonSerializer.Serialize(existing, ToolResponses.JsonOptions);
        File.WriteAllText(configSource.Path, json + Environment.NewLine);

        return ToolResponses.Ok(new
        {
            saved = true,
            name = runtime.Name,
            configPath = configSource.Path
        });
    }

    private bool IsLoadedFor(WorkspaceConfig w) => w.Type switch
    {
        "dotnet" => roslyn.IsLoaded(w.Name),
        "asp-classic" => asp.IsLoaded(w.Name),
        "powershell" => ps.IsLoaded(w.Name),
        "python" => py.IsLoaded(w.Name),
        "javascript" => js.IsLoaded(w.Name),
        _ => false
    };

    [McpServerTool(Name = "refresh_workspace")]
    [Description("Invalidate the in-memory index for a workspace so the next tool call re-indexes from scratch. Use after large file changes or branch switches.")]
    public string RefreshWorkspace(
        [Description("Workspace name from mcp-config.json, or absolute path for ad-hoc workspaces")] string workspace)
    {
        bool any = roslyn.Invalidate(workspace) | asp.Invalidate(workspace)
            | ps.Invalidate(workspace) | py.Invalidate(workspace) | js.Invalidate(workspace);
        return ToolResponses.Ok(new { workspace, refreshed = any });
    }
}
