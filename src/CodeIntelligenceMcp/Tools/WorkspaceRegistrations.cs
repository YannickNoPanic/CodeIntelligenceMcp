namespace CodeIntelligenceMcp.Tools;

public sealed record CleanArchRegistry(IReadOnlyDictionary<string, CleanArchitectureNames> Config);
public sealed record SolutionPathRegistry(IReadOnlyDictionary<string, string> Paths);
