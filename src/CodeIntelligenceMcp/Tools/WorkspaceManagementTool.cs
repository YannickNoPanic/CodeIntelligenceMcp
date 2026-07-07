namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class WorkspaceManagementTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslyn,
    IWorkspaceProvider<AspIndex> asp,
    IWorkspaceProvider<PowerShellIndex> ps,
    IWorkspaceProvider<PythonIndex> py,
    IWorkspaceProvider<JsIndex> js,
    McpConfig config)
{
    [McpServerTool(Name = "list_workspaces")]
    [Description("List all configured workspaces with type, path, and whether they are already indexed. Absolute .sln/.slnx paths also work ad hoc on any dotnet tool.")]
    public string ListWorkspaces()
    {
        var workspaces = config.Workspaces.Select(w => new
        {
            name = w.Name,
            type = w.Type,
            path = w.Solution ?? w.RootPath,
            loaded = IsLoadedFor(w)
        }).ToList();

        return ToolResponses.Ok(new { workspaces });
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
