using System.Text;
using CodeIntelligenceMcp.Roslyn.Models;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class WikiGenerator(RoslynWorkspaceIndex index)
{
    public string Generate(
        string? focusArea = null,
        bool includePatterns = true,
        bool includeMetrics = false,
        bool includeViolations = true,
        bool includeDiagnostics = true,
        CleanArchitectureNames? cleanArch = null)
    {
        IReadOnlyList<TypeSummary> allTypes = index.FindTypes(@namespace: focusArea);
        ProjectDependency projectDep = index.GetProjectDependencies();

        List<(string Name, string Dir)> projectDirs = projectDep.Projects
            .Where(p => !string.IsNullOrEmpty(p.Path))
            .Select(p => (p.Name, Dir: Path.GetDirectoryName(p.Path) ?? string.Empty))
            .Where(p => !string.IsNullOrEmpty(p.Dir))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Codebase Wiki");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        AppendProjectStructure(sb, allTypes, projectDirs);

        if (includePatterns)
            AppendPatterns(sb, focusArea);

        if (includeViolations && cleanArch is not null)
            AppendHealthSummary(sb, cleanArch, focusArea);

        if (includeMetrics)
            AppendMetrics(sb, allTypes, projectDep);

        return sb.ToString();
    }

    private static void AppendProjectStructure(
        StringBuilder sb,
        IReadOnlyList<TypeSummary> types,
        List<(string Name, string Dir)> projectDirs)
    {
        sb.AppendLine("## Project Structure");
        sb.AppendLine();

        if (types.Count == 0)
        {
            sb.AppendLine("_No types found._");
            sb.AppendLine();
            return;
        }

        var typesByProject = new Dictionary<string, List<TypeSummary>>(StringComparer.Ordinal);

        foreach (TypeSummary type in types)
        {
            string projectName = ResolveProject(type.FilePath, projectDirs);

            if (!typesByProject.TryGetValue(projectName, out List<TypeSummary>? list))
            {
                list = [];
                typesByProject[projectName] = list;
            }

            list.Add(type);
        }

        foreach ((string projectName, List<TypeSummary> projectTypes) in typesByProject.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"### {projectName}");

            var byNamespace = projectTypes
                .GroupBy(t => GetNamespaceGroup(t.Namespace, projectName))
                .OrderBy(g => g.Key);

            foreach (var nsGroup in byNamespace)
            {
                int classes = nsGroup.Count(t => t.Kind == "class");
                int interfaces = nsGroup.Count(t => t.Kind == "interface");
                int records = nsGroup.Count(t => t.Kind == "record");
                int enums = nsGroup.Count(t => t.Kind == "enum");

                var parts = new List<string>();
                if (classes > 0) parts.Add($"{classes} class{(classes == 1 ? "" : "es")}");
                if (interfaces > 0) parts.Add($"{interfaces} interface{(interfaces == 1 ? "" : "s")}");
                if (records > 0) parts.Add($"{records} record{(records == 1 ? "" : "s")}");
                if (enums > 0) parts.Add($"{enums} enum{(enums == 1 ? "" : "s")}");

                string summary = string.Join(", ", parts);
                sb.AppendLine($"  └─ {nsGroup.Key}/ [{summary}]");
            }

            sb.AppendLine();
        }
    }

    private void AppendPatterns(StringBuilder sb, string? focusArea)
    {
        sb.AppendLine("## Architectural Patterns");
        sb.AppendLine();

        IReadOnlyList<TypeSummary> useCaseImpls = [..
            index.FindTypes(implementsInterface: "IUseCase", @namespace: focusArea)
                 .Where(t => t.Kind != "interface")];

        if (useCaseImpls.Count > 0)
        {
            sb.AppendLine($"**Use Cases**: {useCaseImpls.Count} implementations");

            var byDomain = useCaseImpls
                .GroupBy(t => GetDomainFromNamespace(t.Namespace))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count())
                .Take(10);

            foreach (var domainGroup in byDomain)
                sb.AppendLine($"  - {domainGroup.Key}: {domainGroup.Count()} use case{(domainGroup.Count() == 1 ? "" : "s")}");

            sb.AppendLine();
        }

        IReadOnlyList<TypeSummary> repositories = [..
            index.FindTypes(nameContains: "Repository", @namespace: focusArea)
                 .Where(t => t.Kind == "class")];

        if (repositories.Count > 0)
        {
            sb.AppendLine($"**Repositories**: {repositories.Count} implementations");
            sb.AppendLine();
        }

        IReadOnlyList<TypeSummary> controllers = [..
            index.FindTypes(nameContains: "Controller", @namespace: focusArea)
                 .Where(t => t.Kind == "class")];

        if (controllers.Count > 0)
        {
            sb.AppendLine($"**Controllers**: {controllers.Count}");
            sb.AppendLine();
        }

        IReadOnlyList<TypeSummary> allTypes = index.FindTypes(@namespace: focusArea);

        var domains = allTypes
            .Select(t => GetDomainFromNamespace(t.Namespace))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d)
            .ToList();

        if (domains.Count > 0)
        {
            sb.AppendLine($"**Vertical Slices**: {domains.Count} feature domain{(domains.Count == 1 ? "" : "s")}");
            foreach (string domain in domains.Take(15))
                sb.AppendLine($"  - {domain}");
            sb.AppendLine();
        }
    }

    private void AppendHealthSummary(StringBuilder sb, CleanArchitectureNames cleanArch, string? focusArea)
    {
        sb.AppendLine("## Health Summary");
        sb.AppendLine();

        ViolationDetector detector = new(index, cleanArch);
        string[] allRules =
        [
            "core-no-ef", "core-no-http", "core-no-azure",
            "usecase-not-sealed",
            "inline-viewmodel-razor", "business-logic-in-razor", "json-parsing-in-view",
            "controller-not-thin", "dto-in-core"
        ];

        bool anyViolation = false;
        foreach (string rule in allRules)
        {
            IReadOnlyList<ViolationResult> violations = detector.Detect(rule);
            if (!string.IsNullOrEmpty(focusArea))
                violations = [.. violations.Where(v => v.FilePath.Contains(focusArea, StringComparison.OrdinalIgnoreCase))];

            if (violations.Count == 0)
                continue;

            anyViolation = true;
            sb.AppendLine($"**{rule}**: {violations.Count} violation{(violations.Count == 1 ? "" : "s")}");
            foreach (ViolationResult v in violations.Take(3))
                sb.AppendLine($"  - {Path.GetFileName(v.FilePath)}:{v.LineNumber} — {v.Description}");
            sb.AppendLine();
        }

        if (!anyViolation)
        {
            sb.AppendLine("No architectural violations detected.");
            sb.AppendLine();
        }
    }

    private static void AppendMetrics(
        StringBuilder sb,
        IReadOnlyList<TypeSummary> types,
        ProjectDependency projectDep)
    {
        sb.AppendLine("## Metrics");
        sb.AppendLine();

        int classes = types.Count(t => t.Kind == "class");
        int interfaces = types.Count(t => t.Kind == "interface");
        int records = types.Count(t => t.Kind == "record");
        int enums = types.Count(t => t.Kind == "enum");

        sb.AppendLine($"**Total Types**: {types.Count}");
        sb.AppendLine($"  - Classes: {classes}");
        sb.AppendLine($"  - Interfaces: {interfaces}");
        sb.AppendLine($"  - Records: {records}");
        sb.AppendLine($"  - Enums: {enums}");
        sb.AppendLine();

        int fileCount = types
            .Select(t => t.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        sb.AppendLine($"**Files**: {fileCount} .cs files");
        sb.AppendLine();

        int testProjects = projectDep.Projects
            .Count(p => p.Name.Contains("Test", StringComparison.OrdinalIgnoreCase));

        if (testProjects > 0)
        {
            sb.AppendLine($"**Test Projects**: {testProjects}");
            sb.AppendLine();
        }
    }

    private static string ResolveProject(string filePath, List<(string Name, string Dir)> projectDirs)
    {
        string normalizedFile = filePath.Replace('\\', '/');
        string bestDir = string.Empty;
        string bestName = "Unknown";

        foreach ((string name, string dir) in projectDirs)
        {
            string normalizedDir = dir.Replace('\\', '/');

            if (normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase)
                && normalizedDir.Length > bestDir.Length)
            {
                bestDir = normalizedDir;
                bestName = name;
            }
        }

        return bestName;
    }

    private static string GetNamespaceGroup(string @namespace, string projectName)
    {
        if (@namespace.StartsWith(projectName + ".", StringComparison.OrdinalIgnoreCase))
        {
            string remainder = @namespace[(projectName.Length + 1)..];
            string[] parts = remainder.Split('.');
            return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
        }

        if (@namespace.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            return "(root)";

        string[] segments = @namespace.Split('.');
        return segments.Length >= 2
            ? $"{segments[^2]}.{segments[^1]}"
            : @namespace;
    }

    private static string GetDomainFromNamespace(string @namespace)
    {
        string[] parts = @namespace.Split('.');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Application", StringComparison.OrdinalIgnoreCase)
                || parts[i].Equals("Features", StringComparison.OrdinalIgnoreCase)
                || parts[i].Equals("UseCases", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1];
            }
        }

        return parts.Length >= 2 ? parts[^2] : string.Empty;
    }
}
