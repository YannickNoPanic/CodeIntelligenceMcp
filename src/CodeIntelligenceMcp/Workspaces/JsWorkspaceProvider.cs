using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class JsWorkspaceProvider(McpConfig config) : IWorkspaceProvider<JsIndex>
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
                return null;

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
        catch
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<JsIndex>>>(cacheKey, lazy));
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

    private static Task<JsIndex> LoadAsync(WorkspaceConfig ws)
    {
        return Task.Run(() =>
        {
            Console.Error.WriteLine($"[info] Loading javascript workspace '{ws.Name}'...");
            Stopwatch sw = Stopwatch.StartNew();
            JsIndex index = JsIndex.Build(ws.RootPath!, msg => Console.Error.WriteLine(msg));
            sw.Stop();
            Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files in {sw.Elapsed.TotalSeconds:F1}s");
            return index;
        });
    }
}
