using System.Text;
using CodeIntelligenceMcp.PowerShell.Models;

namespace CodeIntelligenceMcp.PowerShell;

public sealed class PowerShellWikiGenerator(PowerShellIndex index)
{
    public string Generate(string? focusArea = null, bool includePatterns = true, bool includeMetrics = false)
    {
        IReadOnlyDictionary<string, PowerShellFileInfo> allFiles = index.GetAllFiles();

        IEnumerable<KeyValuePair<string, PowerShellFileInfo>> files = string.IsNullOrEmpty(focusArea)
            ? allFiles
            : allFiles.Where(kvp => kvp.Key.Contains(focusArea, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<PowerShellModuleManifest> manifests = string.IsNullOrEmpty(focusArea)
            ? index.GetModules()
            : index.GetModules().Where(m => m.ManifestPath.Contains(focusArea, StringComparison.OrdinalIgnoreCase)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# PowerShell Project Wiki");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        AppendScriptStructure(sb, files, index.RootPath);

        if (manifests.Any())
            AppendModuleManifests(sb, manifests);

        AppendDependencies(sb, files);

        if (includePatterns)
            AppendPatterns(sb, files);

        if (includeMetrics)
            AppendMetrics(sb, files, manifests);

        return sb.ToString();
    }

    private static void AppendScriptStructure(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PowerShellFileInfo>> files,
        string rootPath)
    {
        sb.AppendLine("## Script Structure");
        sb.AppendLine();

        IOrderedEnumerable<IGrouping<string, KeyValuePair<string, PowerShellFileInfo>>> byDir = files
            .GroupBy(kvp => Path.GetDirectoryName(kvp.Key) ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, KeyValuePair<string, PowerShellFileInfo>> group in byDir)
        {
            string dir = group.Key;
            string relDir = string.IsNullOrEmpty(rootPath)
                ? dir
                : Path.GetRelativePath(rootPath, dir).Replace('\\', '/');

            sb.AppendLine($"### {relDir}/");

            foreach ((string filePath, PowerShellFileInfo fileInfo) in group.OrderBy(kvp => kvp.Key))
            {
                string fileName = Path.GetFileName(filePath);
                sb.AppendLine($"  {fileName}");

                if (fileInfo.Functions.Count > 0)
                {
                    sb.Append("    Functions: ");
                    sb.AppendLine(string.Join(", ", fileInfo.Functions.Select(f => f.Name)));
                }

                if (fileInfo.ImportedModules.Count > 0)
                {
                    sb.Append("    Imports: ");
                    sb.AppendLine(string.Join(", ", fileInfo.ImportedModules.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase)));
                }
            }

            sb.AppendLine();
        }
    }

    private static void AppendModuleManifests(
        StringBuilder sb,
        IReadOnlyList<PowerShellModuleManifest> manifests)
    {
        sb.AppendLine("## Module Manifests");
        sb.AppendLine();

        foreach (PowerShellModuleManifest manifest in manifests.OrderBy(m => m.Name))
        {
            string version = manifest.Version is not null ? $" v{manifest.Version}" : string.Empty;
            sb.AppendLine($"### {manifest.Name}{version}");

            if (!string.IsNullOrEmpty(manifest.Description))
                sb.AppendLine($"  {manifest.Description}");

            if (manifest.ExportedFunctions.Count > 0)
            {
                sb.Append("  Exported Functions: ");
                sb.AppendLine(string.Join(", ", manifest.ExportedFunctions));
            }

            if (manifest.ExportedCmdlets.Count > 0)
            {
                sb.Append("  Exported Cmdlets: ");
                sb.AppendLine(string.Join(", ", manifest.ExportedCmdlets));
            }

            if (manifest.RequiredModules.Count > 0)
            {
                sb.Append("  Requires: ");
                sb.AppendLine(string.Join(", ", manifest.RequiredModules));
            }

            sb.AppendLine();
        }
    }

    private static void AppendDependencies(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PowerShellFileInfo>> files)
    {
        IReadOnlyList<ImportedModule> allImports = files
            .SelectMany(kvp => kvp.Value.ImportedModules)
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(m => m.Version).First())
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allImports.Count == 0)
            return;

        sb.AppendLine("## Dependencies");
        sb.AppendLine();

        foreach (ImportedModule module in allImports)
        {
            string version = module.Version is not null ? $" ({module.Version})" : string.Empty;
            sb.AppendLine($"- {module.Name}{version}");
        }

        sb.AppendLine();
    }

    private static void AppendPatterns(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PowerShellFileInfo>> files)
    {
        List<PowerShellFunctionInfo> allFunctions = files
            .SelectMany(kvp => kvp.Value.Functions)
            .ToList();

        if (allFunctions.Count == 0)
            return;

        sb.AppendLine("## Patterns Detected");
        sb.AppendLine();

        int advancedFunctions = allFunctions.Count(f => f.HasCmdletBinding);
        int pipelineFunctions = allFunctions.Count(f => f.SupportsPipeline);
        int errorHandling = allFunctions.Count(f => f.HasTryCatch);

        sb.AppendLine($"- Advanced Functions (CmdletBinding): {advancedFunctions} of {allFunctions.Count}");
        sb.AppendLine($"- Pipeline Support: {pipelineFunctions} functions");
        sb.AppendLine($"- Error Handling (try/catch): {errorHandling} functions");

        // Top cmdlet usage across all files
        Dictionary<string, int> cmdletTotals = files
            .SelectMany(kvp => kvp.Value.CmdletUsages)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Count));

        if (cmdletTotals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Top Cmdlet Usage:");
            foreach ((string name, int count) in cmdletTotals.OrderByDescending(kvp => kvp.Value).Take(10))
                sb.AppendLine($"  {name}: {count}x");
        }

        sb.AppendLine();
    }

    private static void AppendMetrics(
        StringBuilder sb,
        IEnumerable<KeyValuePair<string, PowerShellFileInfo>> files,
        IReadOnlyList<PowerShellModuleManifest> manifests)
    {
        List<KeyValuePair<string, PowerShellFileInfo>> fileList = files.ToList();

        int ps1Count = fileList.Count(kvp => kvp.Key.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
        int psm1Count = fileList.Count(kvp => kvp.Key.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase));
        int totalFunctions = fileList.Sum(kvp => kvp.Value.Functions.Count);
        int totalVariables = fileList.Sum(kvp => kvp.Value.Variables.Count);

        sb.AppendLine("## Metrics");
        sb.AppendLine();
        sb.AppendLine($"- Scripts (.ps1): {ps1Count}");
        sb.AppendLine($"- Modules (.psm1): {psm1Count}");
        sb.AppendLine($"- Manifests (.psd1): {manifests.Count}");
        sb.AppendLine($"- Total Functions: {totalFunctions}");
        sb.AppendLine($"- Script Variables: {totalVariables}");
        sb.AppendLine();
    }
}
