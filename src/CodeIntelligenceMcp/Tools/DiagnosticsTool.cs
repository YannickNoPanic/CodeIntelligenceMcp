namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class DiagnosticsTool(IWorkspaceProvider<RoslynWorkspaceIndex> roslynProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "get_diagnostics")]
    [Description("Get Roslyn compiler diagnostics for a .NET workspace, grouped by diagnostic code. Use after a refactor to check for new warnings, or as a cleanup starting point. Start with severity 'warning' and filter to one project to reduce noise. Does not duplicate architectural rules from find_violations — covers CS/IDE/CA compiler output only.")]
    public async Task<string> GetDiagnostics(
        [Description("Workspace name from mcp-config.json, or an absolute path to a .sln/.slnx file for ad-hoc worktrees")] string workspace,
        [Description("Minimum severity: 'error' | 'warning' | 'info'. Default 'warning'.")] string? severity = "warning",
        [Description("Filter to one project by exact name (e.g. 'Datalake2.Core')")] string? project = null,
        [Description("Filter by diagnostic code prefix: 'CS', 'IDE', 'CA', 'SA'")] string? category = null,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<DiagnosticResult> diagnostics =
            await index.GetCompilerDiagnosticsAsync(project, severity, category, ct);

        var groups = diagnostics
            .GroupBy(d => d.Id)
            .OrderByDescending(g => g.Count())
            .Take(50)
            .Select(g => new
            {
                id = g.Key,
                severity = g.First().Severity,
                category = g.First().Category,
                count = g.Count(),
                examples = g.Take(3).Select(d => new
                {
                    filePath = d.FilePath,
                    lineNumber = d.LineNumber,
                    projectName = d.ProjectName,
                    message = d.Message
                }).ToArray()
            })
            .ToArray();

        return Ok(new
        {
            workspace,
            filters = new { severity, project, category },
            totalDiagnostics = diagnostics.Count,
            groups
        });
    }
}
