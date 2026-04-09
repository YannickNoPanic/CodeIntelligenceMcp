namespace CodeIntelligenceMcp.Roslyn.Models;

public record ViolationResult(
    string Rule,
    string FilePath,
    int LineNumber,
    string? TypeName,
    string? MethodName,
    string Description);
