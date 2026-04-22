using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;
using Microsoft.Extensions.Logging;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class RoslynWorkspaceProvider(McpConfig config, ILogger<RoslynWorkspaceProvider> logger) : IWorkspaceProvider<RoslynWorkspaceIndex>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<RoslynWorkspaceIndex>>> _loaded =
        new(StringComparer.Ordinal);

    public async Task<RoslynWorkspaceIndex?> GetAsync(string workspace, CancellationToken ct = default)
    {
        WorkspaceConfig ws;

        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = workspace.Replace('\\', '/');
            ws = new WorkspaceConfig { Name = normalizedPath, Type = "dotnet", Solution = normalizedPath };
        }
        else
        {
            WorkspaceConfig? found = config.Workspaces
                .FirstOrDefault(w => w.Name == workspace && w.Type == "dotnet");

            if (found?.Solution is null)
            {
                logger.LogWarning("Workspace '{Workspace}' not found — known dotnet workspaces: {Known}",
                    workspace,
                    string.Join(", ", config.Workspaces.Where(w => w.Type == "dotnet").Select(w => w.Name)));
                return null;
            }

            ws = found;
        }

        string cacheKey = ws.Name;
        Lazy<Task<RoslynWorkspaceIndex>> lazy = _loaded.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<RoslynWorkspaceIndex>>(() => LoadAsync(ws)));

        try
        {
            return await lazy.Value;
        }
        catch (Exception ex)
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<RoslynWorkspaceIndex>>>(cacheKey, lazy));
            logger.LogError(ex, "Failed to load dotnet workspace '{Workspace}'", workspace);
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

    private async Task<RoslynWorkspaceIndex> LoadAsync(WorkspaceConfig ws)
    {
        CleanArchitectureNames cleanArch = ws.CleanArchitecture is not null
            ? new CleanArchitectureNames(
                ws.CleanArchitecture.CoreProject,
                ws.CleanArchitecture.InfraProject,
                ws.CleanArchitecture.WebProject)
            : new CleanArchitectureNames(string.Empty, string.Empty, string.Empty);

        logger.LogInformation("Loading dotnet workspace '{Workspace}'...", ws.Name);
        Stopwatch sw = Stopwatch.StartNew();
        RoslynWorkspaceIndex index = await RoslynLoader.LoadAsync(ws.Solution!, cleanArch);
        sw.Stop();
        logger.LogInformation("Workspace '{Workspace}' loaded — {TypeCount} types in {Seconds:F1}s",
            ws.Name, index.TypeCount, sw.Elapsed.TotalSeconds);
        return index;
    }
}
