namespace CodeIntelligenceMcp.Roslyn.Models;

public record MethodComplexity(
    string TypeName,
    string MethodName,
    string FilePath,
    int LineNumber,
    int Complexity,
    string Label,
    int Lines);
