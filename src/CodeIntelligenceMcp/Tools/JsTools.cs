namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class JsTools(JsIndexRegistry indexes)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    [McpServerTool(Name = "get_js_wiki")]
    [Description("Generate a compact overview of a JavaScript/TypeScript project: modules, components, exports, imports, dependencies, and framework patterns.")]
    public string GetJsWiki(
        [Description("Workspace name")] string workspace,
        [Description("Subdirectory to focus on (e.g. 'src/components' or 'server/api')")] string? focusArea = null,
        [Description("Include pattern analysis (Vue SFC, Nuxt conventions, React components, etc.)")] bool includePatterns = true,
        [Description("Include metrics (file counts, function/class/interface counts)")] bool includeMetrics = false)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out JsIndex? index))
            return Err("workspace not found");

        JsWikiGenerator generator = new(index);
        return generator.Generate(focusArea, includePatterns, includeMetrics);
    }

    [McpServerTool(Name = "js_get_file")]
    [Description("Get the full analysis of a single JS/TS/Vue file: functions, classes, imports, exports, interfaces, or Vue SFC blocks.")]
    public string JsGetFile(
        [Description("Workspace name")] string workspace,
        [Description("File path (absolute or relative to workspace root)")] string filePath)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out JsIndex? index))
            return Err("workspace not found");

        // Try as Vue file first
        if (filePath.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
        {
            VueSfcInfo? vueInfo = index.GetVueFile(filePath);
            if (vueInfo is not null)
                return Ok(vueInfo);
        }

        JsFileInfo? info = index.GetFile(filePath);
        if (info is null)
            return Err("file not found");

        return Ok(info);
    }

    [McpServerTool(Name = "js_find_function")]
    [Description("Find JavaScript/TypeScript functions by name across all files in the workspace, including Vue SFC script blocks.")]
    public string JsFindFunction(
        [Description("Workspace name")] string workspace,
        [Description("Function name or partial name (case-insensitive)")] string functionName)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out JsIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, JsFunctionInfo Function)> results = index.FindFunction(functionName);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            functionName = r.Function.Name,
            lineStart = r.Function.LineStart,
            lineEnd = r.Function.LineEnd,
            kind = r.Function.Kind,
            isExported = r.Function.IsExported,
            isAsync = r.Function.IsAsync,
            isGenerator = r.Function.IsGenerator
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "js_find_class")]
    [Description("Find JavaScript/TypeScript classes by name across all files in the workspace.")]
    public string JsFindClass(
        [Description("Workspace name")] string workspace,
        [Description("Class name or partial name (case-insensitive)")] string className)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out JsIndex? index))
            return Err("workspace not found");

        IReadOnlyList<(string FilePath, JsClassInfo Class)> results = index.FindClass(className);

        object[] mapped = results.Select(r => (object)new
        {
            filePath = r.FilePath,
            className = r.Class.Name,
            lineStart = r.Class.LineStart,
            extends = r.Class.Extends,
            isExported = r.Class.IsExported,
            isAbstract = r.Class.IsAbstract,
            methodCount = r.Class.Methods.Count,
            methods = r.Class.Methods.Select(m => m.Name)
        }).ToArray();

        return Ok(mapped);
    }

    [McpServerTool(Name = "js_search")]
    [Description("Search for a term across function names, class names, exports, and import paths in a JavaScript/TypeScript workspace.")]
    public string JsSearch(
        [Description("Workspace name")] string workspace,
        [Description("Search term")] string query)
    {
        if (!indexes.Indexes.TryGetValue(workspace, out JsIndex? index))
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
