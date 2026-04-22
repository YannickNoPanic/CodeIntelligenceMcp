using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;
using Microsoft.Extensions.Logging;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class PowerShellWorkspaceProvider(McpConfig config, ILogger<PowerShellWorkspaceProvider> logger) : IWorkspaceProvider<PowerShellIndex>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<PowerShellIndex>>> _loaded =
        new(StringComparer.Ordinal);

    public async Task<PowerShellIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig ws;

        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = workspace.Replace('\\', '/');
            ws = new WorkspaceConfig { Name = normalizedPath, Type = "powershell", RootPath = normalizedPath };
        }
        else
        {
            WorkspaceConfig? found = config.Workspaces
                .FirstOrDefault(w => w.Name == workspace && w.Type == "powershell");

            if (found?.RootPath is null)
            {
                logger.LogWarning("Workspace '{Workspace}' not found — known powershell workspaces: {Known}",
                    workspace,
                    string.Join(", ", config.Workspaces.Where(w => w.Type == "powershell").Select(w => w.Name)));
                return null;
            }

            ws = found;
        }

        string cacheKey = ws.Name;
        Lazy<Task<PowerShellIndex>> lazy = _loaded.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<PowerShellIndex>>(() => LoadAsync(ws)));

        try
        {
            return await lazy.Value;
        }
        catch (Exception ex)
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<PowerShellIndex>>>(cacheKey, lazy));
            logger.LogError(ex, "Failed to load powershell workspace '{Workspace}'", workspace);
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

    private Task<PowerShellIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            logger.LogInformation("Loading powershell workspace '{Workspace}'...", ws.Name);
            Stopwatch sw = Stopwatch.StartNew();
            PowerShellIndex index = PowerShellIndex.Build(ws.RootPath!, msg => logger.LogInformation("{Message}", msg));
            sw.Stop();
            logger.LogInformation("Workspace '{Workspace}' loaded — {FileCount} files in {Seconds:F1}s",
                ws.Name, index.FileCount, sw.Elapsed.TotalSeconds);
            return index;
        });
    }
}
