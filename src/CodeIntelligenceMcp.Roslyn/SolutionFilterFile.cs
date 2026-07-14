using System.Text.Json;

namespace CodeIntelligenceMcp.Roslyn;

// Parsed .slnf solution filter: the referenced solution plus the project subset to load.
// Paths are absolute with forward slashes; the set compares case-insensitively (Windows paths).
public sealed record SolutionFilterFile(string SolutionPath, IReadOnlySet<string> ProjectPaths)
{
    public static SolutionFilterFile Parse(string slnfPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(slnfPath)) ?? string.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(slnfPath));
        }
        catch (JsonException ex)
        {
            throw new WorkspaceLoadException(
                $"Solution filter '{slnfPath}' is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("solution", out JsonElement solution)
                || !solution.TryGetProperty("path", out JsonElement pathElement)
                || pathElement.GetString() is not { Length: > 0 } solutionRelPath)
            {
                throw new WorkspaceLoadException(
                    $"Solution filter '{slnfPath}' is missing the solution.path property");
            }

            string solutionPath = Path.GetFullPath(Path.Combine(dir, solutionRelPath));

            HashSet<string> projects = new(StringComparer.OrdinalIgnoreCase);
            if (solution.TryGetProperty("projects", out JsonElement projectsElement))
            {
                foreach (JsonElement project in projectsElement.EnumerateArray())
                {
                    if (project.GetString() is { Length: > 0 } rel)
                        projects.Add(Path.GetFullPath(Path.Combine(dir, rel)).Replace('\\', '/'));
                }
            }

            return new SolutionFilterFile(solutionPath, projects);
        }
    }
}
