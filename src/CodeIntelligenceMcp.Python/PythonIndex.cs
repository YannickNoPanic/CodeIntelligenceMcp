using CodeIntelligenceMcp.Python.Models;

namespace CodeIntelligenceMcp.Python;

public sealed class PythonIndex
{
    private readonly IReadOnlyDictionary<string, PythonFileInfo> _files;
    private readonly PythonProjectInfo _projectInfo;
    private readonly string _rootPath;

    private static readonly string[] SkipDirs =
        ["__pycache__", ".venv", "venv", "env", ".git", ".tox", ".eggs",
         "node_modules", ".pytest_cache", ".mypy_cache", ".ruff_cache",
         "dist", "build", ".nox"];

    private PythonIndex(
        IReadOnlyDictionary<string, PythonFileInfo> files,
        PythonProjectInfo projectInfo,
        string rootPath)
    {
        _files = files;
        _projectInfo = projectInfo;
        _rootPath = rootPath;
    }

    public int FileCount => _files.Count;
    public string RootPath => _rootPath;

    public static PythonIndex Build(string rootPath, Action<string>? log = null)
    {
        var files = new Dictionary<string, PythonFileInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in EnumerateFiles(rootPath, "*.py", SkipDirs))
        {
            try
            {
                string content = File.ReadAllText(filePath);
                PythonFileInfo info = PythonFileParser.Parse(filePath, content);
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

        PythonProjectInfo projectInfo;
        try
        {
            projectInfo = PythonPackageParser.ParseProjectInfo(rootPath);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[warn] Failed to parse project metadata: {ex.Message}");
            projectInfo = new PythonProjectInfo(null, null, [], []);
        }

        log?.Invoke($"[info] Python index built: {files.Count} files");
        return new PythonIndex(files, projectInfo, rootPath);
    }

    public PythonFileInfo? GetFile(string filePath)
    {
        if (_files.TryGetValue(filePath, out PythonFileInfo? info))
            return info;

        // Partial match (relative path)
        string normalized = filePath.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar);
        string? key = _files.Keys.FirstOrDefault(k =>
            k.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));

        return key is not null ? _files[key] : null;
    }

    public IReadOnlyList<(string FilePath, PythonFunctionInfo Function)> FindFunction(string functionName)
    {
        var result = new List<(string, PythonFunctionInfo)>();

        foreach ((string filePath, PythonFileInfo fileInfo) in _files)
        {
            foreach (PythonFunctionInfo func in fileInfo.Functions)
            {
                if (func.Name.Contains(functionName, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, func));
            }

            foreach (PythonClassInfo cls in fileInfo.Classes)
            {
                foreach (PythonFunctionInfo method in cls.Methods)
                {
                    if (method.Name.Contains(functionName, StringComparison.OrdinalIgnoreCase))
                        result.Add((filePath, method));
                }
            }
        }

        return result;
    }

    public IReadOnlyList<(string FilePath, PythonClassInfo Class)> FindClass(string className)
    {
        var result = new List<(string, PythonClassInfo)>();

        foreach ((string filePath, PythonFileInfo fileInfo) in _files)
        {
            foreach (PythonClassInfo cls in fileInfo.Classes)
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

        foreach ((string filePath, PythonFileInfo fileInfo) in _files)
        {
            foreach (PythonFunctionInfo func in fileInfo.Functions)
            {
                if (func.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, func.LineStart, $"function {func.Name}"));
            }

            foreach (PythonClassInfo cls in fileInfo.Classes)
            {
                if (cls.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, cls.LineStart, $"class {cls.Name}"));

                foreach (PythonFunctionInfo method in cls.Methods)
                {
                    if (method.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        result.Add((filePath, method.LineStart, $"method {cls.Name}.{method.Name}"));
                }
            }

            foreach (PythonImportInfo import in fileInfo.Imports)
            {
                if (import.Module.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, import.Line, $"import {import.Module}"));
            }
        }

        return result;
    }

    public PythonProjectInfo GetProjectInfo() => _projectInfo;

    public IReadOnlyDictionary<string, PythonFileInfo> GetAllFiles() => _files;

    private static IEnumerable<string> EnumerateFiles(string rootPath, string pattern, string[] skipDirs)
    {
        return Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
            .Where(f => !skipDirs.Any(skip =>
                f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => string.Equals(segment, skip, StringComparison.OrdinalIgnoreCase))));
    }
}
