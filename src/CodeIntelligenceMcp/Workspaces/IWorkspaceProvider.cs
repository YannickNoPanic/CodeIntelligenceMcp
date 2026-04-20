namespace CodeIntelligenceMcp.Workspaces;

public interface IWorkspaceProvider<TIndex>
{
    Task<TIndex?> GetAsync(string workspace, CancellationToken ct = default);
    bool Invalidate(string workspace);
}
