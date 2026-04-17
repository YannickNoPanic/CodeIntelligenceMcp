namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class AspClassicTools(IWorkspaceProvider<AspIndex> aspProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "asp_get_file")]
    [Description("Get the full structure of a Classic ASP file: includes, subs, functions, variables, and VBScript blocks. Use instead of reading the file directly.")]
    public async Task<string> AspGetFile(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("File path")] string filePath,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        AspFileInfo? fileInfo = index.GetFile(filePath);
        if (fileInfo is null)
            return Err("file not found");

        return Ok(fileInfo);
    }

    [McpServerTool(Name = "asp_find_symbol")]
    [Description("Find subs, functions, variables, or call sites by name across all ASP files. Use to locate where something is defined or called.")]
    public async Task<string> AspFindSymbol(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("Symbol name to find")] string symbolName,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<(string FilePath, int LineNumber, string Kind, string Context)> results = index.FindSymbol(symbolName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            kind = r.Kind,
            context = r.Context
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "asp_get_includes")]
    [Description("Get the include chain for an ASP file: direct includes and transitive includes with depth. Use to understand file dependencies.")]
    public async Task<string> AspGetIncludes(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("File path")] string filePath,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

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

        return Ok(new { filePath, includes = mappedDirect, transitiveIncludes = mappedTransitive });
    }

    [McpServerTool(Name = "asp_search")]
    [Description("Search VBScript content across all ASP files by substring. Use to find where a string, variable, or pattern appears.")]
    public async Task<string> AspSearch(
        [Description("Workspace name from mcp-config.json, or absolute path to an ASP root directory for ad-hoc use")] string workspace,
        [Description("Search query (case-insensitive substring)")] string query,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<(string FilePath, int LineNumber, string Context)> results = index.Search(query);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            context = r.Context
        }).ToArray();

        return Ok(mapped);
    }
}
