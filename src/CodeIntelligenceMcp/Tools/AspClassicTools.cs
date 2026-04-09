namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class AspClassicTools(AspIndexRegistry indexes)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "asp_get_file")]
    public string AspGetFile(
        [Description("Workspace name")] string workspace,
        [Description("File path")] string filePath)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out AspIndex? index))
            return Err("workspace not found");

        AspFileInfo? fileInfo = index.GetFile(filePath);
        if (fileInfo is null)
            return Err("file not found");

        return Ok(fileInfo);
    }

    [McpServerTool(Name = "asp_find_symbol")]
    public string AspFindSymbol(
        [Description("Workspace name")] string workspace,
        [Description("Symbol name to find")] string symbolName)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out AspIndex? index))
            return Err("workspace not found");

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
    public string AspGetIncludes(
        [Description("Workspace name")] string workspace,
        [Description("File path")] string filePath)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out AspIndex? index))
            return Err("workspace not found");

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
    public string AspSearch(
        [Description("Workspace name")] string workspace,
        [Description("Search query (case-insensitive substring)")] string query)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out AspIndex? index))
            return Err("workspace not found");

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
