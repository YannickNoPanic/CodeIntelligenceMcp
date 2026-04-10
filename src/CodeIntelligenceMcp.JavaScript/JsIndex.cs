using CodeIntelligenceMcp.JavaScript.Models;

namespace CodeIntelligenceMcp.JavaScript;

public sealed class JsIndex
{
    private readonly IReadOnlyDictionary<string, JsFileInfo> _files;
    private readonly IReadOnlyList<VueSfcInfo> _vueComponents;
    private readonly JsProjectInfo _projectInfo;
    private readonly string _rootPath;

    private static readonly string[] JsExtensions = ["*.js", "*.ts", "*.jsx", "*.tsx", "*.mjs", "*.cjs"];

    private static readonly string[] SkipDirs =
        ["node_modules", ".git", "dist", "build", ".next", ".nuxt", ".output",
         "coverage", ".nyc_output", "vendor", ".cache", "tmp", "__snapshots__",
         ".turbo", ".vercel", "storybook-static"];

    private JsIndex(
        IReadOnlyDictionary<string, JsFileInfo> files,
        IReadOnlyList<VueSfcInfo> vueComponents,
        JsProjectInfo projectInfo,
        string rootPath)
    {
        _files = files;
        _vueComponents = vueComponents;
        _projectInfo = projectInfo;
        _rootPath = rootPath;
    }

    public int FileCount => _files.Count + _vueComponents.Count;
    public string RootPath => _rootPath;

    public static JsIndex Build(string rootPath, Action<string>? log = null)
    {
        var files = new Dictionary<string, JsFileInfo>(StringComparer.OrdinalIgnoreCase);
        var vueComponents = new List<VueSfcInfo>();

        // Parse JS/TS files
        foreach (string ext in JsExtensions)
        {
            foreach (string filePath in EnumerateFiles(rootPath, ext, SkipDirs))
            {
                try
                {
                    string content = File.ReadAllText(filePath);
                    JsFileInfo info = JsFileParser.Parse(filePath, content);
                    files[filePath] = info;
                }
                catch (IOException ex)
                {
                    log?.Invoke($"[warn] Could not read {filePath}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[warn] Failed to parse {filePath}: {ex.Message}");
                }
            }
        }

        // Parse Vue SFC files
        foreach (string filePath in EnumerateFiles(rootPath, "*.vue", SkipDirs))
        {
            try
            {
                string content = File.ReadAllText(filePath);
                VueSfcInfo info = VueSfcExtractor.Extract(filePath, content);
                vueComponents.Add(info);
            }
            catch (IOException ex)
            {
                log?.Invoke($"[warn] Could not read {filePath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[warn] Failed to parse Vue file {filePath}: {ex.Message}");
            }
        }

        JsProjectInfo projectInfo;
        try
        {
            projectInfo = JsPackageParser.ParseProjectInfo(rootPath);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[warn] Failed to parse package.json: {ex.Message}");
            projectInfo = new JsProjectInfo(null, null, [], [], []);
        }

        log?.Invoke($"[info] JavaScript index built: {files.Count} JS/TS files, {vueComponents.Count} Vue components");
        return new JsIndex(files, vueComponents, projectInfo, rootPath);
    }

    public JsFileInfo? GetFile(string filePath)
    {
        if (_files.TryGetValue(filePath, out JsFileInfo? info))
            return info;

        string normalized = filePath.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar);
        string? key = _files.Keys.FirstOrDefault(k =>
            k.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));

        return key is not null ? _files[key] : null;
    }

    public VueSfcInfo? GetVueFile(string filePath)
    {
        return _vueComponents.FirstOrDefault(v =>
            v.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
            v.FilePath.EndsWith(filePath.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<(string FilePath, JsFunctionInfo Function)> FindFunction(string functionName)
    {
        var result = new List<(string, JsFunctionInfo)>();

        foreach ((string filePath, JsFileInfo fileInfo) in _files)
        {
            foreach (JsFunctionInfo func in fileInfo.Functions)
            {
                if (func.Name.Contains(functionName, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, func));
            }

            foreach (JsClassInfo cls in fileInfo.Classes)
            {
                foreach (JsFunctionInfo method in cls.Methods)
                {
                    if (method.Name.Contains(functionName, StringComparison.OrdinalIgnoreCase))
                        result.Add((filePath, method));
                }
            }
        }

        // Also search Vue SFC script blocks
        foreach (VueSfcInfo vue in _vueComponents)
        {
            if (vue.ScriptAnalysis is null) continue;
            foreach (JsFunctionInfo func in vue.ScriptAnalysis.Functions)
            {
                if (func.Name.Contains(functionName, StringComparison.OrdinalIgnoreCase))
                    result.Add((vue.FilePath, func));
            }
        }

        return result;
    }

    public IReadOnlyList<(string FilePath, JsClassInfo Class)> FindClass(string className)
    {
        var result = new List<(string, JsClassInfo)>();

        foreach ((string filePath, JsFileInfo fileInfo) in _files)
        {
            foreach (JsClassInfo cls in fileInfo.Classes)
            {
                if (cls.Name.Contains(className, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, cls));
            }
        }

        return result;
    }

    public IReadOnlyList<(string FilePath, int Line, string Context)> Search(string query)
    {
        var result = new List<(string, int, string)>();

        foreach ((string filePath, JsFileInfo fileInfo) in _files)
        {
            foreach (JsFunctionInfo func in fileInfo.Functions)
            {
                if (func.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, func.LineStart, $"function {func.Name}"));
            }

            foreach (JsClassInfo cls in fileInfo.Classes)
            {
                if (cls.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, cls.LineStart, $"class {cls.Name}"));
            }

            foreach (JsExportInfo export in fileInfo.Exports)
            {
                if (export.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, export.Line, $"export {export.Name}"));
            }

            foreach (JsImportInfo import in fileInfo.Imports)
            {
                if (import.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, import.Line, $"import from {import.Source}"));
            }

            foreach (JsInterfaceInfo iface in fileInfo.Interfaces)
            {
                if (iface.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, iface.LineStart, $"interface {iface.Name}"));
            }
        }

        foreach (VueSfcInfo vue in _vueComponents)
        {
            string componentName = Path.GetFileNameWithoutExtension(vue.FilePath);
            if (componentName.Contains(query, StringComparison.OrdinalIgnoreCase))
                result.Add((vue.FilePath, 1, $"component {componentName}"));
        }

        return result;
    }

    public JsProjectInfo GetProjectInfo() => _projectInfo;

    public IReadOnlyList<VueSfcInfo> GetVueComponents() => _vueComponents;

    public IReadOnlyDictionary<string, JsFileInfo> GetAllFiles() => _files;

    private static IEnumerable<string> EnumerateFiles(string rootPath, string pattern, string[] skipDirs)
    {
        return Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
            .Where(f => !skipDirs.Any(skip =>
                f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => string.Equals(segment, skip, StringComparison.OrdinalIgnoreCase))));
    }
}
