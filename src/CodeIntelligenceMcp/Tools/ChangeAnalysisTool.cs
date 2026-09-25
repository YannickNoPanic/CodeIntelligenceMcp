namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class ChangeAnalysisTool(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslynProvider,
    CleanArchRegistry cleanArch,
    WorkspaceCatalog catalog)
{
    [McpServerTool(Name = "analyze_changes")]
    [Description("Analyze the git diff between current HEAD and a base branch. Returns changed files with affected types, public API signature changes, architectural violations, and diagnostics scoped to changed code. Use as the first call when reviewing a branch before merge, or after a refactor to check for regressions.")]
    public async Task<string> AnalyzeChanges(
        [Description("Workspace name from mcp-config.json, or an absolute path to a .sln/.slnx/.slnf file for ad-hoc worktrees")] string workspace,
        [Description("Base branch or commit (sha, HEAD~3) to compare against. Default 'main'.")] string? baseBranch = "main",
        [Description("Include public API signature changes for modified files. Default true.")] bool includeSignatures = true,
        [Description("Include Roslyn diagnostics scoped to changed files. Default true.")] bool includeDiagnostics = true,
        [Description("Also include uncommitted working-tree changes (staged + unstaged). Use before commit to preview the full impact. Default false.")] bool includeUncommitted = false,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, catalog, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        string solutionPath = ResolveSolutionPath(workspace);

        if (string.IsNullOrEmpty(solutionPath))
            return ToolResponses.Err($"solution path not found for workspace '{workspace}'");

        // This tool reads the git diff live from disk; the index must match that same state,
        // otherwise violations and diagnostics describe a different snapshot than the diff.
        if (index.IsStale())
        {
            roslynProvider.Invalidate(workspace);
            (index, error) = await WorkspaceAccess.GetAsync(roslynProvider, catalog, "dotnet", workspace, ct);
            if (index is null)
                return error!;
        }

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

            return ToolResponses.Ok(analysis);
        }
        catch (ArgumentException ex)
        {
            return ToolResponses.Err(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResponses.Err(ex.Message);
        }
    }

    private string ResolveSolutionPath(string workspace)
    {
        WorkspaceConfig? resolved = catalog.Resolve(
            "dotnet",
            workspace,
            ws => ws.Solution,
            path => new WorkspaceConfig { Name = path, Type = "dotnet", Solution = path });

        return resolved?.Solution ?? string.Empty;
    }
}
