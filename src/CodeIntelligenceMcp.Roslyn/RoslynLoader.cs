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
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
        }
    }

    public static async Task<RoslynWorkspaceIndex> LoadAsync(
        string solutionPath,
        CleanArchitectureNames cleanArch,
        CancellationToken cancellationToken = default)
    {
        RegisterMSBuild();

        MSBuildWorkspace workspace = MSBuildWorkspace.Create();

        List<string> loadWarnings = [];
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            lock (loadWarnings)
                loadWarnings.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}");
        });

        Microsoft.CodeAnalysis.Solution solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        RoslynWorkspaceIndex index = await RoslynWorkspaceIndex.BuildAsync(workspace, solution, cleanArch, loadWarnings, cancellationToken);
        index.Fingerprint = Git.GitDiffService.ComputeFingerprint(solutionPath);
        return index;
    }
}
