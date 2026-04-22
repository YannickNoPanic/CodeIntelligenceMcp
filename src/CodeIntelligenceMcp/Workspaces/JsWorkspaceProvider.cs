using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;
using Microsoft.Extensions.Logging;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class JsWorkspaceProvider(McpConfig config, ILogger<JsWorkspaceProvider> logger) : IWorkspaceProvider<JsIndex>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<JsIndex>>> _loaded =
        new(StringComparer.Ordinal);

    public async Task<JsIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig ws;

        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = workspace.Replace('\\', '/');
            ws = new WorkspaceConfig { Name = normalizedPath, Type = "javascript", RootPath = normalizedPath };
        }
        else
        {
            WorkspaceConfig? found = config.Workspaces
                .FirstOrDefault(w => w.Name == workspace && w.Type == "javascript");

            if (found?.RootPath is null)
            {
                logger.LogWarning("Workspace '{Workspace}' not found — known javascript workspaces: {Known}",
                    workspace,
                    string.Join(", ", config.Workspaces.Where(w => w.Type == "javascript").Select(w => w.Name)));
                return null;
            }

            ws = found;
        }

        string cacheKey = ws.Name;
        Lazy<Task<JsIndex>> lazy = _loaded.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<JsIndex>>(() => LoadAsync(ws)));

        try
        {
            return await lazy.Value;
        }
        catch (Exception ex)
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<JsIndex>>>(cacheKey, lazy));
            logger.LogError(ex, "Failed to load javascript workspace '{Workspace}'", workspace);
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

    private Task<JsIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            logger.LogInformation("Loading javascript workspace '{Workspace}'...", ws.Name);
            Stopwatch sw = Stopwatch.StartNew();
            JsIndex index = JsIndex.Build(ws.RootPath!, msg => logger.LogInformation("{Message}", msg));
            sw.Stop();
            logger.LogInformation("Workspace '{Workspace}' loaded — {FileCount} files in {Seconds:F1}s",
                ws.Name, index.FileCount, sw.Elapsed.TotalSeconds);
            return index;
        });
    }
}
