namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class CodebaseWikiTool(RoslynIndexRegistry indexes)
{
    [McpServerTool(Name = "get_codebase_wiki")]
    [Description("Generate a compact hierarchical overview of a .NET codebase: project structure, architectural patterns, and optional metrics.")]
    public string GetCodebaseWiki(
        [Description("Workspace name")] string workspace,
        [Description("Namespace prefix to focus on (e.g. 'Datalake2.Core' or 'Application.Orders')")] string? focusArea = null,
        [Description("Include architectural pattern analysis (use cases, repositories, vertical slices)")] bool includePatterns = true,
        [Description("Include metrics (type counts, file counts, test project count)")] bool includeMetrics = false)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out RoslynWorkspaceIndex? index))
            return JsonSerializer.Serialize(new { error = "workspace not found" });

        WikiGenerator generator = new(index);
        return generator.Generate(focusArea, includePatterns, includeMetrics);
    }
}
