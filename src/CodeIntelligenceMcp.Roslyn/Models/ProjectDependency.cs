namespace CodeIntelligenceMcp.Roslyn.Models;

public record ProjectInfo(string Name, string Path);

public record DependencyEdge(string From, string To);

public record ProjectDependency(
    IReadOnlyList<ProjectInfo> Projects,
    IReadOnlyList<DependencyEdge> Dependencies);
