namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class WorkspaceManagementTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslyn,
    IWorkspaceProvider<AspIndex> asp,
    IWorkspaceProvider<PowerShellIndex> ps,
    IWorkspaceProvider<PythonIndex> py,
    IWorkspaceProvider<JsIndex> js)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool(Name = "refresh_workspace")]
    [Description("Invalidate the in-memory index for a workspace so the next tool call re-indexes from scratch. Use after large file changes or branch switches.")]
    public string RefreshWorkspace(
        [Description("Workspace name from mcp-config.json, or absolute path for ad-hoc workspaces")] string workspace)
    {
        bool any = roslyn.Invalidate(workspace) | asp.Invalidate(workspace)
            | ps.Invalidate(workspace) | py.Invalidate(workspace) | js.Invalidate(workspace);
        return JsonSerializer.Serialize(new { workspace, refreshed = any }, JsonOptions);
    }
}
