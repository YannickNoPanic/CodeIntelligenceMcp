namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class AspClassicTools(IWorkspaceProvider<AspIndex> aspProvider, McpConfig config)
{
    [McpServerTool(Name = "asp_get_file")]
    [Description("Get the full structure of a Classic ASP file: includes, subs, functions, variables, and VBScript blocks. Use instead of reading the file directly.")]
    public async Task<string> AspGetFile(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("File path")] string filePath,
        CancellationToken ct = default)
    {
        (AspIndex? index, string? error) = await WorkspaceAccess.GetAsync(aspProvider, config, "asp-classic", workspace, ct);
        if (index is null)
            return error!;

        AspFileInfo? fileInfo = index.GetFile(filePath);
        if (fileInfo is null)
            return ToolResponses.Err("file not found");

        return ToolResponses.Ok(fileInfo);
    }

    [McpServerTool(Name = "asp_find_symbol")]
    [Description("Find subs, functions, variables, or call sites by name across all ASP files. Use to locate where something is defined or called.")]
    public async Task<string> AspFindSymbol(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("Symbol name to find")] string symbolName,
        CancellationToken ct = default)
    {
        (AspIndex? index, string? error) = await WorkspaceAccess.GetAsync(aspProvider, config, "asp-classic", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<(string FilePath, int LineNumber, string Kind, string Context)> results = index.FindSymbol(symbolName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            kind = r.Kind,
            context = r.Context
        }).ToArray();

        return ToolResponses.Ok(mapped);
    }

    [McpServerTool(Name = "asp_get_includes")]
    [Description("Get the include chain for an ASP file: direct includes and transitive includes with depth. Use to understand file dependencies.")]
    public async Task<string> AspGetIncludes(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("File path")] string filePath,
        CancellationToken ct = default)
    {
        (AspIndex? index, string? error) = await WorkspaceAccess.GetAsync(aspProvider, config, "asp-classic", workspace, ct);
        if (index is null)
            return error!;

        (IReadOnlyList<(string Path, string ResolvedPath, int Line, bool Exists)> direct,
         IReadOnlyList<(string Path, string ResolvedPath, int Depth)> transitive) = index.GetIncludes(filePath);

        object[] mappedDirect = direct.Select(i => (object)new
        {
            path = i.Path,
            resolvedPath = i.ResolvedPath,
            line = i.Line,
            exists = i.Exists
        }).ToArray();

        object[] mappedTransitive = transitive.Select(i => (object)new
        {
            path = i.Path,
            resolvedPath = i.ResolvedPath,
            depth = i.Depth
        }).ToArray();

        return ToolResponses.Ok(new { filePath, includes = mappedDirect, transitiveIncludes = mappedTransitive });
    }

    [McpServerTool(Name = "asp_search")]
    [Description("Search VBScript content across all ASP files by substring. Use to find where a string, variable, or pattern appears.")]
    public async Task<string> AspSearch(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("Search query (case-insensitive substring)")] string query,
        CancellationToken ct = default)
    {
        (AspIndex? index, string? error) = await WorkspaceAccess.GetAsync(aspProvider, config, "asp-classic", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<(string FilePath, int LineNumber, string Context)> results = index.Search(query);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            context = r.Context
        }).ToArray();

        return ToolResponses.Ok(mapped);
    }
}
