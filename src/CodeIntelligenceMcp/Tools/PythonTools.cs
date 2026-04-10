namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class PythonTools(PythonIndexRegistry indexes)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "get_python_wiki")]
    [Description("Generate a compact overview of a Python project: modules, classes, functions, imports, dependencies, and framework patterns.")]
    public string GetPythonWiki(
        [Description("Workspace name")] string workspace,
        [Description("Subdirectory or module prefix to focus on (e.g. 'src/api' or 'myapp')")] string? focusArea = null,
        [Description("Include pattern analysis (async usage, Pydantic models, FastAPI routes, etc.)")] bool includePatterns = true,
        [Description("Include metrics (file counts, class/function counts)")] bool includeMetrics = false)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PythonIndex? index))
            return Err("workspace not found");

        PythonWikiGenerator generator = new(index);
        return generator.Generate(focusArea, includePatterns, includeMetrics);
    }

    [McpServerTool(Name = "py_get_file")]
    [Description("Get the full analysis of a single Python file: functions, classes, imports, exports.")]
    public string PyGetFile(
        [Description("Workspace name")] string workspace,
        [Description("File path (absolute or relative to workspace root)")] string filePath)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PythonIndex? index))
            return Err("workspace not found");

        PythonFileInfo? info = index.GetFile(filePath);
        if (info is null)
            return Err("file not found");

        return Ok(info);
    }

    [McpServerTool(Name = "py_find_function")]
    [Description("Find Python functions and methods by name across all files in the workspace.")]
    public string PyFindFunction(
        [Description("Workspace name")] string workspace,
        [Description("Function name or partial name (case-insensitive)")] string functionName)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PythonIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, PythonFunctionInfo Function)> results = index.FindFunction(functionName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            functionName = r.Function.Name,
            lineStart = r.Function.LineStart,
            lineEnd = r.Function.LineEnd,
            isAsync = r.Function.IsAsync,
            isMethod = r.Function.IsMethod,
            decorators = r.Function.Decorators,
            parameters = r.Function.Parameters.Select(p => new
            {
                name = p.Name,
                typeHint = p.TypeHint,
                defaultValue = p.DefaultValue
            }),
            returnTypeHint = r.Function.ReturnTypeHint
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "py_find_class")]
    [Description("Find Python classes by name across all files in the workspace.")]
    public string PyFindClass(
        [Description("Workspace name")] string workspace,
        [Description("Class name or partial name (case-insensitive)")] string className)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PythonIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, PythonClassInfo Class)> results = index.FindClass(className);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            className = r.Class.Name,
            lineStart = r.Class.LineStart,
            baseClasses = r.Class.BaseClasses,
            decorators = r.Class.Decorators,
            methodCount = r.Class.Methods.Count,
            methods = r.Class.Methods.Select(m => m.Name)
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "py_search")]
    [Description("Search for a term across function names, class names, and import paths in a Python workspace.")]
    public string PySearch(
        [Description("Workspace name")] string workspace,
        [Description("Search term")] string query)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out PythonIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, int Line, string Context)> results = index.Search(query);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            line = r.Line,
            context = r.Context
        }).ToArray();

        return Ok(mapped);
    }
}
