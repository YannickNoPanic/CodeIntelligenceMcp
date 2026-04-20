namespace CodeIntelligenceMcp.Roslyn.Models;

public record CallerResult(
    string CallerType,
    string CallerMethod,
    string FilePath,
    int LineNumber,
    string LineText);
