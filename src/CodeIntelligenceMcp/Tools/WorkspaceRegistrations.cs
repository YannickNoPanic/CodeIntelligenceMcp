namespace CodeIntelligenceMcp.Tools;

public sealed record RoslynIndexRegistry(IReadOnlyDictionary<string, RoslynWorkspaceIndex> Indexes);
public sealed record AspIndexRegistry(IReadOnlyDictionary<string, AspIndex> Indexes);
public sealed record PowerShellIndexRegistry(IReadOnlyDictionary<string, PowerShellIndex> Indexes);
public sealed record CleanArchRegistry(IReadOnlyDictionary<string, CleanArchitectureNames> Config);
public sealed record SolutionPathRegistry(IReadOnlyDictionary<string, string> Paths);
