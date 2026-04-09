namespace CodeIntelligenceMcp.AspClassic.Models;

public record VbscriptBlock(int LineStart, int LineEnd, string Source, bool IsExpression = false);
