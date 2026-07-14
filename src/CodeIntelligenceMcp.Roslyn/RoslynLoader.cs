using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeIntelligenceMcp.Roslyn;

public static class RoslynLoader
{
    private static readonly object RegisterLock = new();

    public static void RegisterMSBuild()
    {
        lock (RegisterLock)
        {
            if (MSBuildLocator.IsRegistered)
                return;

            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch (InvalidOperationException ex)
            {
                throw new WorkspaceLoadException(
                    "No compatible MSBuild/.NET SDK found on this machine.",
                    "Install the .NET 10 SDK (or pin a version with global.json). Visual Studio MSBuild also works.",
                    [ex.Message]);
            }
        }
    }

    public static async Task<RoslynWorkspaceIndex> LoadAsync(
        string solutionPath,
        CleanArchitectureNames cleanArch,
        CancellationToken cancellationToken = default)
    {
        RegisterMSBuild();

        // .slnf: open the referenced solution but index only the filtered project subset.
        // The fingerprint keeps using the original path — it only walks up to find .git.
        IReadOnlySet<string>? projectAllowlist = null;
        string openPath = solutionPath;
        if (solutionPath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase))
        {
            SolutionFilterFile filter = SolutionFilterFile.Parse(solutionPath);
            openPath = filter.SolutionPath;
            projectAllowlist = filter.ProjectPaths;
        }
        else if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceLoadException(
                $"'{solutionPath}' is not a .sln, .slnx, or .slnf file",
                "Pass the path to a solution file, a solution filter, or a workspace name from mcp-config.json.");
        }

        MSBuildWorkspace workspace = MSBuildWorkspace.Create();

        List<string> loadWarnings = [];
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            lock (loadWarnings)
                loadWarnings.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}");
        });

        Microsoft.CodeAnalysis.Solution solution;
        try
        {
            solution = await workspace.OpenSolutionAsync(openPath, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WorkspaceLoadException(
                $"Failed to load solution '{openPath}': {ex.Message}",
                "Check that the solution builds with 'dotnet build' on this machine.",
                [.. loadWarnings.Take(3)]);
        }

        RoslynWorkspaceIndex index = await RoslynWorkspaceIndex.BuildAsync(workspace, solution, cleanArch, loadWarnings, cancellationToken, projectAllowlist);
        index.Fingerprint = Git.GitDiffService.ComputeFingerprint(solutionPath);
        return index;
    }
}
