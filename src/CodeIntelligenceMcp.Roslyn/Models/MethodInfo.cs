namespace CodeIntelligenceMcp.Roslyn.Models;

public record MethodInfo(
    string TypeName,
    string MethodName,
    string FilePath,
    int LineStart,
    int LineEnd,
    string Signature,
    string Body);

public record SymbolSearchResult(
    string SymbolName,
    string Kind,
    string TypeName,
    string Namespace,
    string FilePath,
    int LineNumber);
