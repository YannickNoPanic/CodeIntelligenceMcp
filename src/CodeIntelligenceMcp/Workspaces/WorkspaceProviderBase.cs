using System.Collections.Concurrent;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal abstract class WorkspaceProviderBase<TIndex>(McpConfig config, ILogger logger, string workspaceType)
    : IWorkspaceProvider<TIndex>
    where TIndex : class
{
    // OrdinalIgnoreCase: keys are workspace names or Windows paths — "C:/Git/X.sln" and
    // "c:/git/x.sln" must not trigger two full index builds.
    private readonly ConcurrentDictionary<string, Lazy<Task<TIndex>>> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    protected ILogger Logger { get; } = logger;

    protected abstract string? GetConfiguredPath(WorkspaceConfig ws);

    protected abstract WorkspaceConfig CreateAdHoc(string normalizedPath);

    protected abstract Task<TIndex> LoadAsync(WorkspaceConfig ws, CancellationToken ct);

    public async Task<TIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig ws;

        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = workspace.Replace('\\', '/');

            // An absolute path to a configured workspace reuses that workspace's config and
            // cache key — otherwise name-based and path-based calls build two full indexes.
            WorkspaceConfig? configured = config.Workspaces.FirstOrDefault(w =>
                w.Type == workspaceType
                && GetConfiguredPath(w) is string p
                && string.Equals(p.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase));

            ws = configured ?? CreateAdHoc(normalizedPath);
        }
        else
        {
            WorkspaceConfig? found = config.Workspaces
                .FirstOrDefault(w => w.Name == workspace && w.Type == workspaceType);

            if (found is null || GetConfiguredPath(found) is null)
            {
                Logger.LogWarning("Workspace '{Workspace}' not found — known {Type} workspaces: {Known}",
                    workspace,
                    workspaceType,
                    string.Join(", ", config.Workspaces.Where(w => w.Type == workspaceType).Select(w => w.Name)));
                return null;
            }

            ws = found;
        }

        string cacheKey = ws.Name;

        // The shared build runs on CancellationToken.None: one caller's timeout must not kill
        // the index other agents are awaiting. Callers abandon their wait via WaitAsync(ct);
        // a failed build is evicted below so the next call rebuilds.
        Lazy<Task<TIndex>> lazy = _loaded.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<TIndex>>(() => LoadAsync(ws, CancellationToken.None)));

        try
        {
            return await lazy.Value.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (lazy.Value.IsFaulted || lazy.Value.IsCanceled)
                _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<TIndex>>>(cacheKey, lazy));
            throw;
        }
        catch (Exception ex)
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<TIndex>>>(cacheKey, lazy));
            Logger.LogError(ex, "Failed to load {Type} workspace '{Workspace}'", workspaceType, workspace);
            throw;
        }
    }

    public bool IsLoaded(string workspace)
    {
        string cacheKey = Path.IsPathRooted(workspace)
            ? workspace.Replace('\\', '/')
            : workspace;

        return _loaded.TryGetValue(cacheKey, out Lazy<Task<TIndex>>? lazy)
            && lazy.IsValueCreated
            && lazy.Value.IsCompletedSuccessfully;
    }

    public bool Invalidate(string workspace)
    {
        string cacheKey = Path.IsPathRooted(workspace)
            ? workspace.Replace('\\', '/')
            : workspace;

        if (!_loaded.TryRemove(cacheKey, out Lazy<Task<TIndex>>? removed))
            return false;

        DisposeWhenComplete(removed);
        return true;
    }

    private static void DisposeWhenComplete(Lazy<Task<TIndex>> removed)
    {
        if (!removed.IsValueCreated)
            return;

        removed.Value.ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is IDisposable disposable)
                    disposable.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
