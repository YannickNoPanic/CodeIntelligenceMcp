using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class PowerShellWorkspaceProvider(McpConfig config) : IWorkspaceProvider<PowerShellIndex>
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
                return null;

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
        catch
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<PowerShellIndex>>>(cacheKey, lazy));
            throw;
        }
    }

    private static Task<PowerShellIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            Console.Error.WriteLine($"[info] Loading powershell workspace '{ws.Name}'...");
            Stopwatch sw = Stopwatch.StartNew();
            PowerShellIndex index = PowerShellIndex.Build(ws.RootPath!, msg => Console.Error.WriteLine(msg));
            sw.Stop();
            Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files in {sw.Elapsed.TotalSeconds:F1}s");
            return index;
        });
    }
}
