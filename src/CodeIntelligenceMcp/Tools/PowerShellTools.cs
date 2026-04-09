namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class PowerShellTools(PowerShellIndexRegistry indexes)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "get_powershell_wiki")]
    [Description("Generate a compact overview of a PowerShell project: script structure, functions, module manifests, dependencies, and patterns.")]
    public string GetPowerShellWiki(
        [Description("Workspace name")] string workspace,
        [Description("Subdirectory path to focus on (e.g. 'Deploy' or 'Helpers')")] string? focusArea = null,
        [Description("Include pattern analysis (CmdletBinding, pipeline support, error handling)")] bool includePatterns = true,
        [Description("Include metrics (script counts, function counts)")] bool includeMetrics = false)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PowerShellIndex? index))
            return Err("workspace not found");

        PowerShellWikiGenerator generator = new(index);
        return generator.Generate(focusArea, includePatterns, includeMetrics);
    }

    [McpServerTool(Name = "ps_get_file")]
    [Description("Get the full analysis of a single PowerShell script or module file: functions, imports, variables, cmdlet usage.")]
    public string PsGetFile(
        [Description("Workspace name")] string workspace,
        [Description("File path (absolute or relative to workspace root)")] string filePath)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PowerShellIndex? index))
            return Err("workspace not found");

        PowerShellFileInfo? fileInfo = index.GetFile(filePath);
        if (fileInfo is null)
            return Err("file not found");

        return Ok(fileInfo);
    }

    [McpServerTool(Name = "ps_find_function")]
    [Description("Find PowerShell functions by name across all scripts in the workspace.")]
    public string PsFindFunction(
        [Description("Workspace name")] string workspace,
        [Description("Function name or partial name to search for")] string functionName)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PowerShellIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, PowerShellFunctionInfo Function)> results =
            index.FindFunction(functionName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            functionName = r.Function.Name,
            lineStart = r.Function.LineStart,
            lineEnd = r.Function.LineEnd,
            hassCmdletBinding = r.Function.HasCmdletBinding,
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

        return Ok(mapped);
    }

    [McpServerTool(Name = "ps_get_modules")]
    [Description("List all PowerShell module manifests (.psd1) in the workspace with their exported functions and dependencies.")]
    public string PsGetModules(
        [Description("Workspace name")] string workspace)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PowerShellIndex? index))
            return Err("workspace not found");

        return Ok(index.GetModules());
    }

    [McpServerTool(Name = "ps_search")]
    [Description("Search for a term across function names, parameter names, and variables in a PowerShell workspace.")]
    public string PsSearch(
        [Description("Workspace name")] string workspace,
        [Description("Search term")] string query)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PowerShellIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, int LineNumber, string Context)> results =
            index.Search(query);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            lineNumber = r.LineNumber,
            context = r.Context
        }).ToArray();

        return Ok(mapped);
    }
}
