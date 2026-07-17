using System.Collections.Concurrent;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal abstract class WorkspaceProviderBase<TIndex>(WorkspaceCatalog catalog, ILogger logger, string workspaceType)
    : IWorkspaceProvider<TIndex>
    where TIndex : class
{
    // OrdinalIgnoreCase: keys are workspace names or Windows paths, so casing differences
    // must not trigger two full index builds.
    private readonly ConcurrentDictionary<string, Lazy<Task<TIndex>>> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    protected ILogger Logger { get; } = logger;

    protected abstract string? GetConfiguredPath(WorkspaceConfig ws);

    protected abstract WorkspaceConfig CreateAdHoc(string normalizedPath);

    protected abstract Task<TIndex> LoadAsync(WorkspaceConfig ws, CancellationToken ct);

    private WorkspaceConfig? Resolve(string workspace)
    {
        return catalog.Resolve(workspaceType, workspace, GetConfiguredPath, CreateAdHoc);
    }

    public async Task<TIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig? ws = Resolve(workspace);
        if (ws is null)
        {
            Logger.LogWarning("Workspace '{Workspace}' not found - known {Type} workspaces: {Known}",
                workspace,
                workspaceType,
                string.Join(", ", catalog.KnownNames(workspaceType)));
            return null;
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
        string cacheKey = Resolve(workspace)?.Name ?? workspace;

        return _loaded.TryGetValue(cacheKey, out Lazy<Task<TIndex>>? lazy)
            && lazy.IsValueCreated
            && lazy.Value.IsCompletedSuccessfully;
    }

    public bool Invalidate(string workspace)
    {
        string cacheKey = Resolve(workspace)?.Name ?? workspace;

        return _loaded.TryRemove(cacheKey, out _);
    }
}
