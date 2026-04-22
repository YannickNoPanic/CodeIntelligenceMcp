using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;
using Microsoft.Extensions.Logging;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class PythonWorkspaceProvider(McpConfig config, ILogger<PythonWorkspaceProvider> logger) : IWorkspaceProvider<PythonIndex>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<PythonIndex>>> _loaded =
        new(StringComparer.Ordinal);

    public async Task<PythonIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig ws;

        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = workspace.Replace('\\', '/');
            ws = new WorkspaceConfig { Name = normalizedPath, Type = "python", RootPath = normalizedPath };
        }
        else
        {
            WorkspaceConfig? found = config.Workspaces
                .FirstOrDefault(w => w.Name == workspace && w.Type == "python");

            if (found?.RootPath is null)
            {
                logger.LogWarning("Workspace '{Workspace}' not found — known python workspaces: {Known}",
                    workspace,
                    string.Join(", ", config.Workspaces.Where(w => w.Type == "python").Select(w => w.Name)));
                return null;
            }

            ws = found;
        }

        string cacheKey = ws.Name;
        Lazy<Task<PythonIndex>> lazy = _loaded.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<PythonIndex>>(() => LoadAsync(ws)));

        try
        {
            return await lazy.Value;
        }
        catch (Exception ex)
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<PythonIndex>>>(cacheKey, lazy));
            logger.LogError(ex, "Failed to load python workspace '{Workspace}'", workspace);
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

    private Task<PythonIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            logger.LogInformation("Loading python workspace '{Workspace}'...", ws.Name);
            Stopwatch sw = Stopwatch.StartNew();
            PythonIndex index = PythonIndex.Build(ws.RootPath!, msg => logger.LogInformation("{Message}", msg));
            sw.Stop();
            logger.LogInformation("Workspace '{Workspace}' loaded — {FileCount} files in {Seconds:F1}s",
                ws.Name, index.FileCount, sw.Elapsed.TotalSeconds);
            return index;
        });
    }
}
