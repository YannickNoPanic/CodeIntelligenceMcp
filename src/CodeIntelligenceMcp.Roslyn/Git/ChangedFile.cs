namespace CodeIntelligenceMcp.Roslyn.Git;

public sealed record ChangedFile(string FilePath, string Status, string? OldPath);
