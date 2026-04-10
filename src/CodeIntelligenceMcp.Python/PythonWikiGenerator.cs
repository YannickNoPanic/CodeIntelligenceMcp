using System.Text;
using CodeIntelligenceMcp.Python.Models;

namespace CodeIntelligenceMcp.Python;

public sealed class PythonWikiGenerator(PythonIndex index)
{
    public string Generate(string? focusArea = null, bool includePatterns = true, bool includeMetrics = false)
    {
        IReadOnlyDictionary<string, PythonFileInfo> allFiles = index.GetAllFiles();

        IEnumerable<KeyValuePair<string, PythonFileInfo>> files = string.IsNullOrEmpty(focusArea)
            ? allFiles
            : allFiles.Where(kvp => kvp.Key.Contains(focusArea, StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        sb.AppendLine("# Python Project Wiki");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        AppendModuleStructure(sb, files, index.RootPath);
        AppendDependencies(sb, index.GetProjectInfo());

        if (includePatterns)
            AppendPatterns(sb, files, index.GetProjectInfo());

        if (includeMetrics)
            AppendMetrics(sb, files);

        return sb.ToString();
    }

    private static void AppendModuleStructure(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PythonFileInfo>> files,
        string rootPath)
    {
        sb.AppendLine("## Module Structure");
        sb.AppendLine();

        IOrderedEnumerable<IGrouping<string, KeyValuePair<string, PythonFileInfo>>> byDir = files
            .GroupBy(kvp => Path.GetDirectoryName(kvp.Key) ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, KeyValuePair<string, PythonFileInfo>> group in byDir)
        {
            string relDir = string.IsNullOrEmpty(rootPath)
                ? group.Key
                : Path.GetRelativePath(rootPath, group.Key).Replace('\\', '/');

            sb.AppendLine($"### {relDir}/");

            foreach ((string filePath, PythonFileInfo fileInfo) in group.OrderBy(kvp => kvp.Key))
            {
                string fileName = Path.GetFileName(filePath);
                sb.AppendLine($"  {fileName}");

                if (fileInfo.Functions.Count > 0)
                {
                    sb.Append("    Functions: ");
                    sb.AppendLine(string.Join(", ", fileInfo.Functions.Select(f => f.Name)));
                }

                if (fileInfo.Classes.Count > 0)
                {
                    foreach (PythonClassInfo cls in fileInfo.Classes)
                    {
                        string bases = cls.BaseClasses.Count > 0
                            ? $" ({string.Join(", ", cls.BaseClasses)})"
                            : string.Empty;
                        string methods = cls.Methods.Count > 0
                            ? $" [{cls.Methods.Count} methods]"
                            : string.Empty;
                        sb.AppendLine($"    Class: {cls.Name}{bases}{methods}");
                    }
                }

                if (fileInfo.Imports.Count > 0)
                {
                    IEnumerable<string> topModules = fileInfo.Imports
                        .Select(imp => imp.Module.Split('.')[0].TrimStart('.'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(m => !string.IsNullOrEmpty(m))
                        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                        .Take(8);

                    sb.Append("    Imports: ");
                    sb.AppendLine(string.Join(", ", topModules));
                }

                if (fileInfo.ExportedNames.Count > 0)
                {
                    sb.Append("    Exports (__all__): ");
                    sb.AppendLine(string.Join(", ", fileInfo.ExportedNames.Take(8)));
                }
            }

            sb.AppendLine();
        }
    }

    private static void AppendDependencies(StringBuilder sb, PythonProjectInfo projectInfo)
    {
        if (projectInfo.Dependencies.Count == 0 && projectInfo.DevDependencies.Count == 0)
            return;

        sb.AppendLine("## Dependencies");
        sb.AppendLine();

        foreach (PythonPackageInfo pkg in projectInfo.Dependencies.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            string version = pkg.VersionConstraint is not null ? $" ({pkg.VersionConstraint})" : string.Empty;
            sb.AppendLine($"- {pkg.Name}{version} [{pkg.Source}]");
        }

        if (projectInfo.DevDependencies.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Dev/Optional:**");
            foreach (PythonPackageInfo pkg in projectInfo.DevDependencies.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                string version = pkg.VersionConstraint is not null ? $" ({pkg.VersionConstraint})" : string.Empty;
                sb.AppendLine($"- {pkg.Name}{version} [{pkg.Source}]");
            }
        }

        sb.AppendLine();
    }

    private static void AppendPatterns(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PythonFileInfo>> files,
        PythonProjectInfo projectInfo)
    {
        List<KeyValuePair<string, PythonFileInfo>> fileList = files.ToList();
        List<PythonFunctionInfo> allFunctions = fileList
            .SelectMany(kvp => kvp.Value.Functions
                .Concat(kvp.Value.Classes.SelectMany(c => c.Methods)))
            .ToList();

        if (allFunctions.Count == 0 && fileList.Count == 0)
            return;

        sb.AppendLine("## Patterns Detected");
        sb.AppendLine();

        // Frameworks
        HashSet<string> allFrameworks = fileList
            .SelectMany(kvp => kvp.Value.DetectedFrameworks)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allFrameworks.Count > 0)
        {
            foreach (string fw in allFrameworks.OrderBy(f => f))
                sb.AppendLine($"- Framework: {fw}");
        }

        if (projectInfo.PythonVersion is not null)
            sb.AppendLine($"- Python Version: {projectInfo.PythonVersion}");

        int asyncCount = allFunctions.Count(f => f.IsAsync);
        if (asyncCount > 0)
            sb.AppendLine($"- Async Functions: {asyncCount} of {allFunctions.Count}");

        // Classes with base classes (Pydantic models, Django models, etc.)
        List<PythonClassInfo> allClasses = fileList.SelectMany(kvp => kvp.Value.Classes).ToList();
        int classesWithBases = allClasses.Count(c => c.BaseClasses.Count > 0);
        if (classesWithBases > 0)
            sb.AppendLine($"- Classes with Inheritance: {classesWithBases} of {allClasses.Count}");

        // Functions with type hints
        int typedFunctions = allFunctions.Count(f =>
            f.Parameters.Any(p => p.TypeHint is not null) || f.ReturnTypeHint is not null);
        if (allFunctions.Count > 0)
            sb.AppendLine($"- Type-Annotated Functions: {typedFunctions} of {allFunctions.Count}");

        // __all__ exports
        int modulesWithAll = fileList.Count(kvp => kvp.Value.ExportedNames.Count > 0);
        if (modulesWithAll > 0)
            sb.AppendLine($"- Modules with __all__: {modulesWithAll}");

        // Decorated functions (FastAPI routes, etc.)
        int decorated = allFunctions.Count(f => f.Decorators.Count > 0);
        if (decorated > 0)
            sb.AppendLine($"- Decorated Functions/Methods: {decorated}");

        sb.AppendLine();
    }

    private static void AppendMetrics(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PythonFileInfo>> files)
    {
        List<KeyValuePair<string, PythonFileInfo>> fileList = files.ToList();
        int pyFiles = fileList.Count;
        int initFiles = fileList.Count(kvp => Path.GetFileName(kvp.Key) == "__init__.py");
        int totalClasses = fileList.Sum(kvp => kvp.Value.Classes.Count);
        int totalFunctions = fileList.Sum(kvp => kvp.Value.Functions.Count);
        int totalMethods = fileList.Sum(kvp => kvp.Value.Classes.Sum(c => c.Methods.Count));

        sb.AppendLine("## Metrics");
        sb.AppendLine();
        sb.AppendLine($"- Python Files (.py): {pyFiles}");
        sb.AppendLine($"- Packages (__init__.py): {initFiles}");
        sb.AppendLine($"- Classes: {totalClasses}");
        sb.AppendLine($"- Functions (top-level): {totalFunctions}");
        sb.AppendLine($"- Methods (in classes): {totalMethods}");
        sb.AppendLine();
    }
}
