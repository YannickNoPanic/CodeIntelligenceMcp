using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;
using Microsoft.Extensions.Logging;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class AspWorkspaceProvider(McpConfig config, ILogger<AspWorkspaceProvider> logger) : IWorkspaceProvider<AspIndex>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<AspIndex>>> _loaded =
        new(StringComparer.Ordinal);

    public async Task<AspIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig ws;

        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = workspace.Replace('\\', '/');
            ws = new WorkspaceConfig { Name = normalizedPath, Type = "asp-classic", RootPath = normalizedPath };
        }
        else
        {
            WorkspaceConfig? found = config.Workspaces
                .FirstOrDefault(w => w.Name == workspace && w.Type == "asp-classic");

            if (found?.RootPath is null)
            {
                logger.LogWarning("Workspace '{Workspace}' not found — known asp-classic workspaces: {Known}",
                    workspace,
                    string.Join(", ", config.Workspaces.Where(w => w.Type == "asp-classic").Select(w => w.Name)));
                return null;
            }

            ws = found;
        }

        string cacheKey = ws.Name;
        Lazy<Task<AspIndex>> lazy = _loaded.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<AspIndex>>(() => LoadAsync(ws)));

        try
        {
            return await lazy.Value;
        }
        catch (Exception ex)
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<AspIndex>>>(cacheKey, lazy));
            logger.LogError(ex, "Failed to load asp-classic workspace '{Workspace}'", workspace);
            throw;
        }
    }

    public bool Invalidate(string workspace)
    {
        string cacheKey = Path.IsPathRooted(workspace)
            ? workspace.Replace('\\', '/')
            : workspace;
        return _loaded.TryRemove(cacheKey, out _);
    }

    private Task<AspIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            logger.LogInformation("Loading asp-classic workspace '{Workspace}'...", ws.Name);
            Stopwatch sw = Stopwatch.StartNew();
            AspIndex index = AspIndex.Build(ws.RootPath!, msg => logger.LogInformation("{Message}", msg));
            sw.Stop();
            logger.LogInformation("Workspace '{Workspace}' loaded — {FileCount} files in {Seconds:F1}s",
                ws.Name, index.FileCount, sw.Elapsed.TotalSeconds);
            return index;
        });
    }
}
