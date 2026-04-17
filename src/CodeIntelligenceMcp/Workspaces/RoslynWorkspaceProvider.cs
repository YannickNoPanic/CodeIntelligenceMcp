using System.Collections.Concurrent;
using System.Diagnostics;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class RoslynWorkspaceProvider(McpConfig config) : IWorkspaceProvider<RoslynWorkspaceIndex>
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
                return null;

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
        catch
        {
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<RoslynWorkspaceIndex>>>(cacheKey, lazy));
            throw;
        }
    }

    private static async Task<RoslynWorkspaceIndex> LoadAsync(WorkspaceConfig ws)
    {
        CleanArchitectureNames cleanArch = ws.CleanArchitecture is not null
            ? new CleanArchitectureNames(
                ws.CleanArchitecture.CoreProject,
                ws.CleanArchitecture.InfraProject,
                ws.CleanArchitecture.WebProject)
            : new CleanArchitectureNames(string.Empty, string.Empty, string.Empty);

        Console.Error.WriteLine($"[info] Loading dotnet workspace '{ws.Name}'...");
        Stopwatch sw = Stopwatch.StartNew();
        RoslynWorkspaceIndex index = await RoslynLoader.LoadAsync(ws.Solution!, cleanArch);
        sw.Stop();
        Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.TypeCount} types in {sw.Elapsed.TotalSeconds:F1}s");
        return index;
    }
}
