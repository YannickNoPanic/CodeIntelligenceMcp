namespace CodeIntelligenceMcp.Roslyn.Models;

public record HotspotResult(
    string TypeName,
    string FilePath,
    int LineNumber,
    int HotspotScore,
    int Coupling,
    int MaxComplexity,
    bool HasTests,
    string Reason);
