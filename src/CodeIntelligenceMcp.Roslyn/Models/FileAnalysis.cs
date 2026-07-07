namespace CodeIntelligenceMcp.Roslyn.Models;

public sealed record FileObservation(string Kind, string Location, string Detail);

public sealed record FileAnalysis(
    string FilePath,
    string FileType,
    IReadOnlyList<FileObservation> Observations);
