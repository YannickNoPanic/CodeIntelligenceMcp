using System.Diagnostics;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class FileWalkWorkspaceProvider<TIndex>(
    WorkspaceCatalog catalog,
    ILogger logger,
    string workspaceType,
    Func<string, Action<string>?, CancellationToken, TIndex> build,
    Func<TIndex, int> fileCount)
    : WorkspaceProviderBase<TIndex>(catalog, logger, workspaceType)
    where TIndex : class
{
    private readonly string _workspaceType = workspaceType;

    protected override string? GetConfiguredPath(WorkspaceConfig ws) => ws.RootPath;

    protected override WorkspaceConfig CreateAdHoc(string normalizedPath) =>
        new() { Name = normalizedPath, Type = _workspaceType, RootPath = normalizedPath };

    protected override Task<TIndex> LoadAsync(WorkspaceConfig ws, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            Logger.LogInformation("Loading {Type} workspace '{Workspace}'...", _workspaceType, ws.Name);
            Stopwatch sw = Stopwatch.StartNew();
            TIndex index = build(ws.RootPath!, msg => Logger.LogInformation("{Message}", msg), ct);
            sw.Stop();
            Logger.LogInformation("Workspace '{Workspace}' loaded — {FileCount} files in {Seconds:F1}s",
                ws.Name, fileCount(index), sw.Elapsed.TotalSeconds);
            return index;
        }, ct);
    }
}
