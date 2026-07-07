using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Tools;

namespace CodeIntelligenceMcp.Workspaces;

// Single boundary between tools and workspace providers: every failure mode becomes a short
// error JSON with an actionable hint, never a stack trace or the MCP SDK's generic message.
internal static class WorkspaceAccess
{
    public static async Task<(TIndex? Index, string? Error)> GetAsync<TIndex>(
        IWorkspaceProvider<TIndex> provider,
        McpConfig config,
        string workspaceType,
        string workspace,
        CancellationToken ct)
        where TIndex : class
    {
        try
        {
            TIndex? index = await provider.GetAsync(workspace, ct);
            if (index is not null)
                return (index, null);

            string known = string.Join(", ", config.Workspaces
                .Where(w => w.Type == workspaceType)
                .Select(w => w.Name));

            string hint = known.Length > 0
                ? $"known {workspaceType} workspaces: {known} — or pass an absolute path"
                : $"no {workspaceType} workspaces configured — pass an absolute path or add one to mcp-config.json";

            return (null, ToolResponses.Err($"workspace '{workspace}' not found", hint));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceLoadException ex)
        {
            return (null, ToolResponses.Err(ex.Message, ex.Hint, ex.Detail));
        }
        catch (Exception ex)
        {
            return (null, ToolResponses.Err(
                $"failed to load workspace '{workspace}': {ex.Message}",
                "see the server log for details"));
        }
    }
}
