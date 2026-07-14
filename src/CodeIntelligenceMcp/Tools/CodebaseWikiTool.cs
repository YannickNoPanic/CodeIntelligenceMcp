namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class CodebaseWikiTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslynProvider,
    CleanArchRegistry cleanArch,
    McpConfig config)
{
    [McpServerTool(Name = "get_codebase_wiki")]
    [Description("Generate a compact hierarchical overview of a .NET codebase: project structure, architectural patterns, health summary (violations), and optional metrics. Call this first in any session to understand what you are working with. When focusArea is set, violations and patterns are scoped to that namespace.")]
    public async Task<string> GetCodebaseWiki(
        [Description("Workspace name from mcp-config.json, or an absolute path to a .sln/.slnx/.slnf file for ad-hoc worktrees")] string workspace,
        [Description("Namespace prefix to focus on (e.g. 'MyApp.Core' or 'Features.Devices')")] string? focusArea = null,
        [Description("Include architectural pattern analysis (use cases, repositories, vertical slices)")] bool includePatterns = true,
        [Description("Include architectural violations summary in health section. Default true.")] bool includeViolations = true,
        [Description("Include metrics (type counts, file counts, test project count)")] bool includeMetrics = false,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        CleanArchitectureNames configured = cleanArch.Config.GetValueOrDefault(workspace, new CleanArchitectureNames("", "", ""));
        CleanArchitectureNames ca = string.IsNullOrEmpty(configured.CoreProject) ? index.CleanArchitecture : configured;
        WikiGenerator generator = new(index);
        string wiki = await generator.GenerateAsync(focusArea, includePatterns, includeMetrics, includeViolations, ca, ct);

        if (index.IsStaleCached())
        {
            wiki = $"> STALE: files changed since this index was built ({index.IndexedAtUtc:yyyy-MM-dd HH:mm:ss} UTC). "
                + "Call refresh_workspace for current results.\n\n" + wiki;
        }

        return wiki;
    }
}
