namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class PowerShellTools(IWorkspaceProvider<PowerShellIndex> psProvider, WorkspaceCatalog catalog)
{
    [McpServerTool(Name = "get_powershell_wiki")]
    [Description("Generate a compact overview of a PowerShell project: script structure, functions, module manifests, dependencies, and patterns.")]
    public async Task<string> GetPowerShellWiki(
        [Description("Workspace name from mcp-config.json, or absolute path to a PS root directory for ad-hoc use")] string workspace,
        [Description("Subdirectory path to focus on (e.g. 'Deploy' or 'Helpers')")] string? focusArea = null,
        [Description("Include pattern analysis (CmdletBinding, pipeline support, error handling)")] bool includePatterns = true,
        [Description("Include metrics (script counts, function counts)")] bool includeMetrics = false,
        CancellationToken ct = default)
    {
        (PowerShellIndex? index, string? error) = await WorkspaceAccess.GetAsync(psProvider, catalog, "powershell", workspace, ct);
        if (index is null)
            return error!;

        PowerShellWikiGenerator generator = new(index);
        return generator.Generate(focusArea, includePatterns, includeMetrics);
    }

    [McpServerTool(Name = "ps_get_file")]
    [Description("Get the full analysis of a single PowerShell script or module file: functions, imports, variables, cmdlet usage.")]
    public async Task<string> PsGetFile(
        [Description("Workspace name from mcp-config.json, or absolute path to a PS root directory for ad-hoc use")] string workspace,
        [Description("File path (absolute or relative to workspace root)")] string filePath,
        CancellationToken ct = default)
    {
        (PowerShellIndex? index, string? error) = await WorkspaceAccess.GetAsync(psProvider, catalog, "powershell", workspace, ct);
        if (index is null)
            return error!;

        PowerShellFileInfo? fileInfo = index.GetFile(filePath);
        if (fileInfo is null)
            return ToolResponses.Err("file not found");

        return ToolResponses.Ok(fileInfo);
    }

    [McpServerTool(Name = "ps_find_function")]
    [Description("Find PowerShell functions by name across all scripts in the workspace.")]
    public async Task<string> PsFindFunction(
        [Description("Workspace name from mcp-config.json, or absolute path to a PS root directory for ad-hoc use")] string workspace,
        [Description("Function name: substring, or glob with * and ? (case-insensitive)")] string functionName,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (PowerShellIndex? index, string? error) = await WorkspaceAccess.GetAsync(psProvider, catalog, "powershell", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<(string FilePath, PowerShellFunctionInfo Function)> results =
            index.FindFunction(functionName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            functionName = r.Function.Name,
            lineStart = r.Function.LineStart,
            lineEnd = r.Function.LineEnd,
            hasCmdletBinding = r.Function.HasCmdletBinding,
            supportsPipeline = r.Function.SupportsPipeline,
            hasTryCatch = r.Function.HasTryCatch,
            parameters = r.Function.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                isMandatory = p.IsMandatory,
                isFromPipeline = p.IsFromPipeline,
                defaultValue = p.DefaultValue
            })
        }).ToArray();

        return ToolResponses.OkList(mapped, maxResults);
    }

    [McpServerTool(Name = "ps_get_modules")]
    [Description("List all PowerShell module manifests (.psd1) in the workspace with their exported functions and dependencies.")]
    public async Task<string> PsGetModules(
        [Description("Workspace name from mcp-config.json, or absolute path to a PS root directory for ad-hoc use")] string workspace,
        CancellationToken ct = default)
    {
        (PowerShellIndex? index, string? error) = await WorkspaceAccess.GetAsync(psProvider, catalog, "powershell", workspace, ct);
        if (index is null)
            return error!;

        return ToolResponses.Ok(index.GetModules());
    }

    [McpServerTool(Name = "ps_search")]
    [Description("Search for a term across function names, parameter names, and variables in a PowerShell workspace.")]
    public async Task<string> PsSearch(
        [Description("Workspace name from mcp-config.json, or absolute path to a PS root directory for ad-hoc use")] string workspace,
        [Description("Search term: substring, or glob with * and ? (case-insensitive)")] string query,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (PowerShellIndex? index, string? error) = await WorkspaceAccess.GetAsync(psProvider, catalog, "powershell", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<(string FilePath, int LineNumber, string Context)> results =
            index.Search(query);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            context = r.Context
        }).ToArray();

        return ToolResponses.OkList(mapped, maxResults);
    }
}
