using CodeIntelligenceMcp.PowerShell.Models;

namespace CodeIntelligenceMcp.PowerShell;

public sealed class PowerShellIndex
{
    private readonly IReadOnlyDictionary<string, PowerShellFileInfo> _files;
    private readonly IReadOnlyList<PowerShellModuleManifest> _manifests;
    private readonly string _rootPath;

    private PowerShellIndex(
        IReadOnlyDictionary<string, PowerShellFileInfo> files,
        IReadOnlyList<PowerShellModuleManifest> manifests,
        string rootPath)
    {
        _files = files;
        _manifests = manifests;
        _rootPath = rootPath;
    }

    public int FileCount => _files.Count;

    public static PowerShellIndex Build(string rootPath, Action<string>? log = null)
    {
        var files = new Dictionary<string, PowerShellFileInfo>(StringComparer.OrdinalIgnoreCase);
        var manifests = new List<PowerShellModuleManifest>();

        string[] scriptExtensions = ["*.ps1", "*.psm1"];
        string[] skipDirs = [".git", "node_modules", ".vs", ".idea", "bin", "obj"];

        foreach (string filePath in EnumerateFiles(rootPath, scriptExtensions, skipDirs))
        {
            try
            {
                string content = File.ReadAllText(filePath);
                PowerShellFileInfo info = PowerShellScriptParser.Parse(filePath, content);
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

        foreach (string filePath in EnumerateFiles(rootPath, ["*.psd1"], skipDirs))
        {
            try
            {
                string content = File.ReadAllText(filePath);
                PowerShellModuleManifest? manifest = PowerShellManifestParser.Parse(
                    filePath, content, log);

                if (manifest is not null)
                    manifests.Add(manifest);
            }
            catch (IOException ex)
            {
                log?.Invoke($"[warn] Could not read {filePath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[warn] Failed to parse manifest {filePath}: {ex.Message}");
            }
        }

        log?.Invoke($"[info] PowerShell index built: {files.Count} scripts, {manifests.Count} manifests");

        return new PowerShellIndex(files, manifests, rootPath);
    }

    public PowerShellFileInfo? GetFile(string filePath)
    {
        if (_files.TryGetValue(filePath, out PowerShellFileInfo? info))
            return info;

        // Try case-insensitive partial match for convenience
        string normalized = filePath.Replace('/', '\\');
        string? key = _files.Keys.FirstOrDefault(k =>
            k.Replace('/', '\\').EndsWith(normalized, StringComparison.OrdinalIgnoreCase));

        return key is not null ? _files[key] : null;
    }

    public IReadOnlyList<(string FilePath, PowerShellFunctionInfo Function)> FindFunction(string functionName)
    {
        var result = new List<(string, PowerShellFunctionInfo)>();

        foreach ((string filePath, PowerShellFileInfo fileInfo) in _files)
        {
            foreach (PowerShellFunctionInfo func in fileInfo.Functions)
            {
                if (func.Name.Contains(functionName, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, func));
            }
        }

        return result;
    }

    public IReadOnlyList<PowerShellModuleManifest> GetModules() => _manifests;

    public IReadOnlyList<ImportedModule> GetAllImportedModules()
    {
        return _files.Values
            .SelectMany(f => f.ImportedModules)
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(m => m.Version).First())
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<(string FilePath, int LineNumber, string Context)> Search(string query)
    {
        var result = new List<(string, int, string)>();

        foreach ((string filePath, PowerShellFileInfo fileInfo) in _files)
        {
            // Search in function names
            foreach (PowerShellFunctionInfo func in fileInfo.Functions)
            {
                if (func.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, func.LineStart, $"function {func.Name}"));
            }

            // Search in parameter names
            foreach (PowerShellFunctionInfo func in fileInfo.Functions)
            {
                foreach (PowerShellParameterInfo param in func.Parameters)
                {
                    if (param.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        result.Add((filePath, func.LineStart, $"parameter ${param.Name} in {func.Name}"));
                }
            }

            // Search in variable names
            foreach (ScriptVariable variable in fileInfo.Variables)
            {
                if (variable.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add((filePath, variable.Line, $"variable ${variable.Name}"));
            }
        }

        return result;
    }

    public IReadOnlyDictionary<string, PowerShellFileInfo> GetAllFiles() => _files;

    public string RootPath => _rootPath;

    private static IEnumerable<string> EnumerateFiles(
        string rootPath,
        string[] patterns,
        string[] skipDirs)
    {
        return patterns.SelectMany(pattern =>
            Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
                .Where(f => !skipDirs.Any(skip =>
                    f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(segment => string.Equals(segment, skip, StringComparison.OrdinalIgnoreCase)))));
    }
}
