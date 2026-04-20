namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class ChangeAnalysisTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslynProvider,
    CleanArchRegistry cleanArch,
    SolutionPathRegistry solutionPaths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "analyze_changes")]
    [Description("Analyze the git diff between current HEAD and a base branch. Returns changed files with affected types, public API signature changes, architectural violations, and diagnostics scoped to changed code. Use as the first call when reviewing a branch before merge, or after a refactor to check for regressions.")]
    public async Task<string> AnalyzeChanges(
        [Description("Workspace name from mcp-config.json, or an absolute path to a .sln/.slnx file for ad-hoc worktrees")] string workspace,
        [Description("Base branch to compare against. Default 'main'.")] string? baseBranch = "main",
        [Description("Include public API signature changes for modified files. Default true.")] bool includeSignatures = true,
        [Description("Include Roslyn diagnostics scoped to changed files. Default true.")] bool includeDiagnostics = true,
        [Description("Also include uncommitted working-tree changes (staged + unstaged). Use before commit to preview the full impact. Default false.")] bool includeUncommitted = false,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        string solutionPath = Path.IsPathRooted(workspace)
            ? workspace.Replace('\\', '/')
            : solutionPaths.Paths.GetValueOrDefault(workspace, string.Empty);

        if (string.IsNullOrEmpty(solutionPath))
            return Err($"solution path not found for workspace '{workspace}'");

        CleanArchitectureNames configured = cleanArch.Config.GetValueOrDefault(workspace, new CleanArchitectureNames("", "", ""));
        CleanArchitectureNames ca = string.IsNullOrEmpty(configured.CoreProject) ? index.CleanArchitecture : configured;
        ChangeAnalyzer analyzer = new(index, ca);

        try
        {
            ChangeAnalysis analysis = await analyzer.AnalyzeAsync(
                workspace,
                solutionPath,
                baseBranch ?? "main",
                includeSignatures,
                includeDiagnostics,
                includeUncommitted,
                ct);

            return Ok(analysis);
        }
        catch (ArgumentException ex)
        {
            return Err(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Err(ex.Message);
        }
    }
}
