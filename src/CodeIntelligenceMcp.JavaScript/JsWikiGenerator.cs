using System.Text;
using CodeIntelligenceMcp.JavaScript.Models;

namespace CodeIntelligenceMcp.JavaScript;

public sealed class JsWikiGenerator(JsIndex index)
{
    public string Generate(string? focusArea = null, bool includePatterns = true, bool includeMetrics = false)
    {
        IReadOnlyDictionary<string, JsFileInfo> allFiles = index.GetAllFiles();
        IReadOnlyList<VueSfcInfo> vueComponents = index.GetVueComponents();

        IEnumerable<KeyValuePair<string, JsFileInfo>> files = string.IsNullOrEmpty(focusArea)
            ? allFiles
            : allFiles.Where(kvp => kvp.Key.Contains(focusArea, StringComparison.OrdinalIgnoreCase));

        IEnumerable<VueSfcInfo> vues = string.IsNullOrEmpty(focusArea)
            ? vueComponents
            : vueComponents.Where(v => v.FilePath.Contains(focusArea, StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        sb.AppendLine("# JavaScript/TypeScript Project Wiki");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        AppendModuleStructure(sb, files, vues, index.RootPath);
        AppendDependencies(sb, index.GetProjectInfo());

        if (includePatterns)
            AppendPatterns(sb, files, vues, index.GetProjectInfo());

        if (includeMetrics)
            AppendMetrics(sb, files, vues);

        return sb.ToString();
    }

    private static void AppendModuleStructure(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, JsFileInfo>> files,
        IEnumerable<VueSfcInfo> vues,
        string rootPath)
    {
        sb.AppendLine("## Module Structure");
        sb.AppendLine();

        // Merge JS files and Vue files by directory
        var allByDir = files
            .Select(kvp => (Path: kvp.Key, Kind: "js", Js: (JsFileInfo?)kvp.Value, Vue: (VueSfcInfo?)null))
            .Concat(vues.Select(v => (Path: v.FilePath, Kind: "vue", Js: (JsFileInfo?)null, Vue: (VueSfcInfo?)v)))
            .GroupBy(x => Path.GetDirectoryName(x.Path) ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, (string Path, string Kind, JsFileInfo? Js, VueSfcInfo? Vue)> group in allByDir)
        {
            string relDir = string.IsNullOrEmpty(rootPath)
                ? group.Key
                : Path.GetRelativePath(rootPath, group.Key).Replace('\\', '/');

            sb.AppendLine($"### {relDir}/");

            foreach ((string filePath, string kind, JsFileInfo? jsInfo, VueSfcInfo? vueInfo) in group.OrderBy(x => x.Path))
            {
                string fileName = Path.GetFileName(filePath);

                if (kind == "vue" && vueInfo is not null)
                {
                    string scriptLang = vueInfo.Blocks
                        .FirstOrDefault(b => b.Tag == "script")?.Lang ?? string.Empty;
                    string scriptSuffix = string.IsNullOrEmpty(scriptLang) ? string.Empty : $" + {scriptLang}";
                    bool isSetup = vueInfo.Blocks.Any(b => b.Tag == "script" && b.IsSetup);
                    string sfcLabel = isSetup ? $"[SFC: script-setup{scriptSuffix}]" : "[SFC]";

                    sb.AppendLine($"  {fileName} {sfcLabel}");

                    if (vueInfo.Props.Count > 0)
                    {
                        sb.Append("    Props: ");
                        sb.AppendLine(string.Join(", ", vueInfo.Props));
                    }

                    if (vueInfo.Emits.Count > 0)
                    {
                        sb.Append("    Emits: ");
                        sb.AppendLine(string.Join(", ", vueInfo.Emits));
                    }

                    if (vueInfo.Composables.Count > 0)
                    {
                        sb.Append("    Composables: ");
                        sb.AppendLine(string.Join(", ", vueInfo.Composables));
                    }

                    if (vueInfo.ScriptAnalysis?.Imports.Count > 0)
                    {
                        IEnumerable<string> sources = vueInfo.ScriptAnalysis.Imports
                            .Select(i => i.Source)
                            .Take(5);
                        sb.Append("    Imports: ");
                        sb.AppendLine(string.Join(", ", sources));
                    }
                }
                else if (jsInfo is not null)
                {
                    sb.AppendLine($"  {fileName}");

                    if (jsInfo.Functions.Count > 0)
                    {
                        sb.Append("    Functions: ");
                        sb.AppendLine(string.Join(", ", jsInfo.Functions.Select(f => f.Name).Take(8)));
                    }

                    if (jsInfo.Classes.Count > 0)
                    {
                        sb.Append("    Classes: ");
                        sb.AppendLine(string.Join(", ", jsInfo.Classes.Select(c => c.Name)));
                    }

                    if (jsInfo.Interfaces.Count > 0)
                    {
                        sb.Append("    Interfaces: ");
                        sb.AppendLine(string.Join(", ", jsInfo.Interfaces.Select(i => i.Name).Take(6)));
                    }

                    if (jsInfo.Exports.Count > 0)
                    {
                        IEnumerable<string> exportNames = jsInfo.Exports
                            .Where(e => e.Name != "default" && e.Kind != "re-export")
                            .Select(e => e.Name)
                            .Distinct()
                            .Take(6);
                        if (exportNames.Any())
                        {
                            sb.Append("    Exports: ");
                            sb.AppendLine(string.Join(", ", exportNames));
                        }
                    }

                    if (jsInfo.Imports.Count > 0)
                    {
                        IEnumerable<string> sources = jsInfo.Imports
                            .Select(i => i.Source)
                            .Take(5);
                        sb.Append("    Imports: ");
                        sb.AppendLine(string.Join(", ", sources));
                    }
                }
            }

            sb.AppendLine();
        }
    }

    private static void AppendDependencies(StringBuilder sb, JsProjectInfo projectInfo)
    {
        List<JsPackageInfo> prodDeps = projectInfo.Dependencies.Where(d => !d.IsDev).ToList();
        List<JsPackageInfo> devDeps = projectInfo.Dependencies.Where(d => d.IsDev).ToList();

        if (prodDeps.Count == 0 && devDeps.Count == 0)
            return;

        sb.AppendLine("## Dependencies");
        sb.AppendLine();

        foreach (JsPackageInfo pkg in prodDeps.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            string version = pkg.Version is not null ? $" ({pkg.Version})" : string.Empty;
            sb.AppendLine($"- {pkg.Name}{version} [package.json]");
        }

        if (devDeps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Dev:**");
            foreach (JsPackageInfo pkg in devDeps.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(20))
            {
                string version = pkg.Version is not null ? $" ({pkg.Version})" : string.Empty;
                sb.AppendLine($"- {pkg.Name}{version} [devDependencies]");
            }
        }

        sb.AppendLine();
    }

    private static void AppendPatterns(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, JsFileInfo>> files,
        IEnumerable<VueSfcInfo> vues,
        JsProjectInfo projectInfo)
    {
        sb.AppendLine("## Patterns Detected");
        sb.AppendLine();

        // Frameworks
        foreach (string fw in projectInfo.DetectedFrameworks.OrderBy(f => f))
            sb.AppendLine($"- Framework: {fw}");

        List<KeyValuePair<string, JsFileInfo>> fileList = files.ToList();
        List<VueSfcInfo> vueList = vues.ToList();

        int tsFiles = fileList.Count(kvp =>
            kvp.Key.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            kvp.Key.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
        int jsFiles = fileList.Count - tsFiles;

        if (tsFiles > 0 || jsFiles > 0)
            sb.AppendLine($"- TypeScript: {tsFiles} of {fileList.Count} JS/TS files");

        int esmFiles = fileList.Count(kvp => kvp.Value.ModuleType is "esm" or "mixed");
        int cjsFiles = fileList.Count(kvp => kvp.Value.ModuleType is "commonjs" or "mixed");
        if (esmFiles > 0) sb.AppendLine($"- ESM Imports: {esmFiles} files");
        if (cjsFiles > 0) sb.AppendLine($"- CommonJS Require: {cjsFiles} files");

        List<JsFunctionInfo> allFunctions = fileList
            .SelectMany(kvp => kvp.Value.Functions
                .Concat(kvp.Value.Classes.SelectMany(c => c.Methods)))
            .ToList();
        int asyncFunctions = allFunctions.Count(f => f.IsAsync);
        if (asyncFunctions > 0)
            sb.AppendLine($"- Async Functions: {asyncFunctions} of {allFunctions.Count}");

        if (vueList.Count > 0)
        {
            int setupComponents = vueList.Count(v => v.Blocks.Any(b => b.Tag == "script" && b.IsSetup));
            sb.AppendLine($"- Vue SFC Components: {vueList.Count} (.vue files)");
            if (setupComponents > 0)
                sb.AppendLine($"- Script Setup: {setupComponents} of {vueList.Count} components");
        }

        // Nuxt conventions
        AppendNuxtConventions(sb, fileList, vueList);

        sb.AppendLine();
    }

    private static void AppendNuxtConventions(
        StringBuilder sb,
        List<KeyValuePair<string, JsFileInfo>> files,
        List<VueSfcInfo> vues)
    {
        var nuxtSections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string segment in new[] { "pages", "composables", "stores", "layouts", "middleware", "plugins" })
        {
            int count = files.Count(f => ContainsSegment(f.Key, segment)) +
                        vues.Count(v => ContainsSegment(v.FilePath, segment));
            if (count > 0)
                nuxtSections[segment] = count;
        }

        // server/api
        int serverApiCount = files.Count(f => f.Key.Contains("server/api", StringComparison.OrdinalIgnoreCase) ||
                                              f.Key.Contains("server\\api", StringComparison.OrdinalIgnoreCase));
        if (serverApiCount > 0)
            nuxtSections["server/api"] = serverApiCount;

        if (nuxtSections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Nuxt Conventions:");
            foreach ((string dir, int count) in nuxtSections.OrderBy(kv => kv.Key))
                sb.AppendLine($"  {dir}/: {count} files");
        }
    }

    private static bool ContainsSegment(string path, string segment)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendMetrics(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, JsFileInfo>> files,
        IEnumerable<VueSfcInfo> vues)
    {
        List<KeyValuePair<string, JsFileInfo>> fileList = files.ToList();
        List<VueSfcInfo> vueList = vues.ToList();

        int jsCount = fileList.Count(kvp =>
            kvp.Key.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            kvp.Key.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
            kvp.Key.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
            kvp.Key.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase));
        int tsCount = fileList.Count - jsCount;
        int totalFunctions = fileList.Sum(kvp => kvp.Value.Functions.Count);
        int totalClasses = fileList.Sum(kvp => kvp.Value.Classes.Count);
        int totalInterfaces = fileList.Sum(kvp => kvp.Value.Interfaces.Count);
        int totalTypeAliases = fileList.Sum(kvp => kvp.Value.TypeAliases.Count);
        int totalExports = fileList.Sum(kvp => kvp.Value.Exports.Count(e => e.Name != "default" && e.Kind != "re-export"));

        sb.AppendLine("## Metrics");
        sb.AppendLine();
        sb.AppendLine($"- JavaScript (.js/.jsx/.mjs): {jsCount}");
        sb.AppendLine($"- TypeScript (.ts/.tsx): {tsCount}");
        sb.AppendLine($"- Vue SFC (.vue): {vueList.Count}");
        sb.AppendLine($"- Classes: {totalClasses}");
        sb.AppendLine($"- Functions: {totalFunctions}");
        sb.AppendLine($"- Interfaces: {totalInterfaces}");
        sb.AppendLine($"- Type Aliases: {totalTypeAliases}");
        sb.AppendLine($"- Named Exports: {totalExports}");
        sb.AppendLine();
    }
}
