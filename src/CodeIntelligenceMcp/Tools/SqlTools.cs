namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class SqlTools(IWorkspaceProvider<AspIndex> aspProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "sql_find_table")]
    [Description("Find all SQL queries that reference a given table across Classic ASP files. Returns operation type, signature, and columns used.")]
    public async Task<string> SqlFindTable(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        [Description("Table name to search for")] string tableName,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
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
    [Description("Get all normalised SQL query signatures from a single ASP file. Use to review what queries a file executes.")]
    public async Task<string> SqlGetSignatures(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        [Description("File path")] string filePath,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err("SQL tools require an asp-classic workspace");

        IReadOnlyList<SqlQueryInfo> results = index.GetFileQueries(filePath);
        return Ok(results);
    }

    [McpServerTool(Name = "sql_find_column")]
    [Description("Find all SQL queries that reference a given column across Classic ASP files.")]
    public async Task<string> SqlFindColumn(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        [Description("Column name to search for")] string columnName,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
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
    [Description("List all tables referenced by SQL queries in a Classic ASP workspace, sorted by usage count. Use to understand data access patterns.")]
    public async Task<string> SqlListTables(
        [Description("Workspace name (must be an asp-classic workspace)")] string workspace,
        CancellationToken ct = default)
    {
        AspIndex? index = await aspProvider.GetAsync(workspace, ct);
        if (index is null)
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
