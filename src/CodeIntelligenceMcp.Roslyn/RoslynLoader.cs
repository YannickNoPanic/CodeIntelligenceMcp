using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeIntelligenceMcp.Roslyn;

public static class RoslynLoader
{
    public static void RegisterMSBuild()
    {
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
    }

    public static async Task<RoslynWorkspaceIndex> LoadAsync(
        string solutionPath,
        CleanArchitectureNames cleanArch,
        CancellationToken cancellationToken = default)
    {
        MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Microsoft.CodeAnalysis.Solution solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        return await RoslynWorkspaceIndex.BuildAsync(workspace, solution, cleanArch, cancellationToken);
    }
}
