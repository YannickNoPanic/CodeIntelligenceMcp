namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class SqlTools(AspIndexRegistry aspIndexes)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    private bool TryGetAspIndex(string workspace, out AspIndex index)
    {
        if (aspIndexes.Indexes.TryGetValue(workspace, out AspIndex? found))
        {
            index = found;
            return true;
        }

        index = null!;
        return false;
    }

    [McpServerTool(Name = "sql_find_table")]
    public string SqlFindTable(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        [Description("Table name to search for")] string tableName)
    {
        if (!TryGetAspIndex(workspace, out AspIndex index))
            return Err("SQL tools require an asp-classic workspace");

        IReadOnlyList<(string FilePath, SqlQueryInfo Query)> results = index.FindByTable(tableName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineStart = r.Query.LineStart,
            operation = r.Query.Operation,
            signature = r.Query.Signature,
            columns = r.Query.Columns
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "sql_get_signatures")]
    public string SqlGetSignatures(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        [Description("File path")] string filePath)
    {
        if (!TryGetAspIndex(workspace, out AspIndex index))
            return Err("SQL tools require an asp-classic workspace");

        IReadOnlyList<SqlQueryInfo> results = index.GetFileQueries(filePath);
        return Ok(results);
    }

    [McpServerTool(Name = "sql_find_column")]
    public string SqlFindColumn(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        [Description("Column name to search for")] string columnName)
    {
        if (!TryGetAspIndex(workspace, out AspIndex index))
            return Err("SQL tools require an asp-classic workspace");

        IReadOnlyList<(string FilePath, int LineNumber, string? TableName, string Operation, string Signature)> results =
            index.FindByColumn(columnName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            tableName = r.TableName,
            operation = r.Operation,
            signature = r.Signature
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "sql_list_tables")]
    public string SqlListTables(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace)
    {
        if (!TryGetAspIndex(workspace, out AspIndex index))
            return Err("SQL tools require an asp-classic workspace");

        IReadOnlyList<(string TableName, int UsageCount, IReadOnlyList<(string FilePath, int UsageCount)> Files)> results =
            index.ListTables();

        object[] mapped = results.Select(r => (object)new
        {
            tableName = r.TableName,
            usageCount = r.UsageCount,
            files = r.Files.Select(f => new { filePath = f.FilePath, usageCount = f.UsageCount }).ToArray()
        }).ToArray();

        return Ok(mapped);
    }
}
