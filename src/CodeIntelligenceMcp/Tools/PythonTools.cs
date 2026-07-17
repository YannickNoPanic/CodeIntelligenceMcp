namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class PythonTools(IWorkspaceProvider<PythonIndex> pythonProvider, WorkspaceCatalog catalog)
{
    [McpServerTool(Name = "get_python_wiki")]
    [Description("Generate a compact overview of a Python project: modules, classes, functions, imports, dependencies, and framework patterns.")]
    public async Task<string> GetPythonWiki(
        [Description("Workspace name")] string workspace,
        [Description("Subdirectory or module prefix to focus on (e.g. 'src/api' or 'myapp')")] string? focusArea = null,
        [Description("Include pattern analysis (async usage, Pydantic models, FastAPI routes, etc.)")] bool includePatterns = true,
        [Description("Include metrics (file counts, class/function counts)")] bool includeMetrics = false,
        CancellationToken ct = default)
    {
        (PythonIndex? index, string? error) = await WorkspaceAccess.GetAsync(pythonProvider, catalog, "python", workspace, ct);
        if (index is null)
            return error!;

        PythonWikiGenerator generator = new(index);
        return generator.Generate(focusArea, includePatterns, includeMetrics);
    }

    [McpServerTool(Name = "py_get_file")]
    [Description("Get the full analysis of a single Python file: functions, classes, imports, exports.")]
    public async Task<string> PyGetFile(
        [Description("Workspace name")] string workspace,
        [Description("File path (absolute or relative to workspace root)")] string filePath,
        CancellationToken ct = default)
    {
        (PythonIndex? index, string? error) = await WorkspaceAccess.GetAsync(pythonProvider, catalog, "python", workspace, ct);
        if (index is null)
            return error!;

        PythonFileInfo? info = index.GetFile(filePath);
        if (info is null)
            return ToolResponses.Err("file not found");

        return ToolResponses.Ok(info);
    }

    [McpServerTool(Name = "py_find_function")]
    [Description("Find Python functions and methods by name across all files in the workspace.")]
    public async Task<string> PyFindFunction(
        [Description("Workspace name")] string workspace,
        [Description("Function name: substring, or glob with * and ? (case-insensitive)")] string functionName,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (PythonIndex? index, string? error) = await WorkspaceAccess.GetAsync(pythonProvider, catalog, "python", workspace, ct);
        if (index is null)
            return error!;

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

        return ToolResponses.OkList(mapped, maxResults);
    }

    [McpServerTool(Name = "py_find_class")]
    [Description("Find Python classes by name across all files in the workspace.")]
    public async Task<string> PyFindClass(
        [Description("Workspace name")] string workspace,
        [Description("Class name: substring, or glob with * and ? (case-insensitive)")] string className,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (PythonIndex? index, string? error) = await WorkspaceAccess.GetAsync(pythonProvider, catalog, "python", workspace, ct);
        if (index is null)
            return error!;

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

        return ToolResponses.OkList(mapped, maxResults);
    }

    [McpServerTool(Name = "py_search")]
    [Description("Search for a term across function names, class names, and import paths in a Python workspace.")]
    public async Task<string> PySearch(
        [Description("Workspace name")] string workspace,
        [Description("Search term: substring, or glob with * and ? (case-insensitive)")] string query,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (PythonIndex? index, string? error) = await WorkspaceAccess.GetAsync(pythonProvider, catalog, "python", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<(string FilePath, int Line, string Context)> results = index.Search(query);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            line = r.Line,
            context = r.Context
        }).ToArray();

        return ToolResponses.OkList(mapped, maxResults);
    }
}
