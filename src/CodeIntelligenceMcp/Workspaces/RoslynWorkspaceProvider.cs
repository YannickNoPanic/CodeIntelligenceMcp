using System.Diagnostics;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class RoslynWorkspaceProvider(McpConfig config, ILogger<RoslynWorkspaceProvider> logger)
    : WorkspaceProviderBase<RoslynWorkspaceIndex>(config, logger, "dotnet")
{
    protected override string? GetConfiguredPath(WorkspaceConfig ws) => ws.Solution;

    protected override WorkspaceConfig CreateAdHoc(string normalizedPath) =>
        new() { Name = normalizedPath, Type = "dotnet", Solution = normalizedPath };

    protected override async Task<RoslynWorkspaceIndex> LoadAsync(WorkspaceConfig ws, CancellationToken ct)
    {
        CleanArchitectureNames cleanArch = ws.CleanArchitecture is not null
            ? new CleanArchitectureNames(
                ws.CleanArchitecture.CoreProject,
                ws.CleanArchitecture.InfraProject,
                ws.CleanArchitecture.WebProject)
            : new CleanArchitectureNames(string.Empty, string.Empty, string.Empty);

        Logger.LogInformation("Loading dotnet workspace '{Workspace}'...", ws.Name);
        Stopwatch sw = Stopwatch.StartNew();
        RoslynWorkspaceIndex index = await RoslynLoader.LoadAsync(ws.Solution!, cleanArch, ct);
        sw.Stop();
        Logger.LogInformation("Workspace '{Workspace}' loaded — {TypeCount} types in {Seconds:F1}s",
            ws.Name, index.TypeCount, sw.Elapsed.TotalSeconds);
        return index;
    }
}
