namespace CodeIntelligenceMcp.Roslyn.Models;

public record DeadCodeResult(
    string TypeName,
    string MemberName,
    string MemberKind,
    string FilePath,
    int LineNumber);
