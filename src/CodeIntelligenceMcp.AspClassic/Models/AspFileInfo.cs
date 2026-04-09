namespace CodeIntelligenceMcp.AspClassic.Models;

public record IncludeRef(string Path, int Line);
public record SubInfo(string Name, IReadOnlyList<string> Parameters, int LineStart, int LineEnd);
public record FunctionInfo(string Name, IReadOnlyList<string> Parameters, int LineStart, int LineEnd);
public record VariableInfo(string Name, int Line);

public record AspFileInfo(
    string FilePath,
    IReadOnlyList<IncludeRef> Includes,
    IReadOnlyList<SubInfo> Subs,
    IReadOnlyList<FunctionInfo> Functions,
    IReadOnlyList<VariableInfo> Variables,
    IReadOnlyList<VbscriptBlock> VbscriptBlocks
);
