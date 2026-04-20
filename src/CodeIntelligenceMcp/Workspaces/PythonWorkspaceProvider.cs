using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class PythonWorkspaceProvider(McpConfig config) : IWorkspaceProvider<PythonIndex>
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
                return null;

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
        catch
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<PythonIndex>>>(cacheKey, lazy));
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

    private static Task<PythonIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            Console.Error.WriteLine($"[info] Loading python workspace '{ws.Name}'...");
            Stopwatch sw = Stopwatch.StartNew();
            PythonIndex index = PythonIndex.Build(ws.RootPath!, msg => Console.Error.WriteLine(msg));
            sw.Stop();
            Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files in {sw.Elapsed.TotalSeconds:F1}s");
            return index;
        });
    }
}
