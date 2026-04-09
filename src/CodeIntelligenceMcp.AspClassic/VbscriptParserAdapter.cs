using System.Text.RegularExpressions;
using CodeIntelligenceMcp.AspClassic.Models;
using VBScript.Parser;
using VBScript.Parser.Ast;
using VBScript.Parser.Ast.Statements;

namespace CodeIntelligenceMcp.AspClassic;

public record ParsedBlock(
    IReadOnlyList<SubInfo> Subs,
    IReadOnlyList<FunctionInfo> Functions,
    IReadOnlyList<VariableInfo> Variables
);

public static class VbscriptParserAdapter
{
    private static readonly Regex SubPattern = new(
        @"^\s*(?:Public\s+|Private\s+)?Sub\s+(\w+)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FunctionPattern = new(
        @"^\s*(?:Public\s+|Private\s+)?Function\s+(\w+)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VariablePattern = new(
        @"^\s*Dim\s+(\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EndSubPattern = new(
        @"^\s*End\s+Sub\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EndFunctionPattern = new(
        @"^\s*End\s+Function\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedBlock Parse(string source, int lineOffset, Action<string>? log = null)
    {
        try
        {
            return ParseWithAst(source, lineOffset);
        }
        catch (VBSyntaxErrorException ex)
        {
            string snippet = source.Length > 80 ? source[..80] + "..." : source;
            log?.Invoke($"[warn] VBScript parse failed at line {ex.Line}, col {ex.Position} ({ex.Code}): {snippet.ReplaceLineEndings(" ")}");
            return ParseWithRegex(source, lineOffset);
        }
        catch (Exception ex)
        {
            string snippet = source.Length > 80 ? source[..80] + "..." : source;
            log?.Invoke($"[warn] VBScript parse failed ({ex.GetType().Name}): {ex.Message} — {snippet.ReplaceLineEndings(" ")}");
            return ParseWithRegex(source, lineOffset);
        }
    }

    private static ParsedBlock ParseWithAst(string source, int lineOffset)
    {
        VBScriptParser parser = new(source);
        Program program = parser.Parse();

        var subs = new List<SubInfo>();
        var functions = new List<FunctionInfo>();
        var variables = new List<VariableInfo>();

        foreach (Statement statement in program.Body)
        {
            if (statement is SubDeclaration sub)
            {
                int lineStart = lineOffset + sub.Location.Start.Line - 1;
                int lineEnd = lineOffset + sub.Location.End.Line - 1;
                IReadOnlyList<string> parameters = [.. sub.Parameters.Select(p => p.Identifier.Name)];
                subs.Add(new SubInfo(sub.Identifier.Name, parameters, lineStart, lineEnd));
            }
            else if (statement is FunctionDeclaration func)
            {
                int lineStart = lineOffset + func.Location.Start.Line - 1;
                int lineEnd = lineOffset + func.Location.End.Line - 1;
                IReadOnlyList<string> parameters = [.. func.Parameters.Select(p => p.Identifier.Name)];
                functions.Add(new FunctionInfo(func.Identifier.Name, parameters, lineStart, lineEnd));
            }
            else if (statement is VariablesDeclaration varDecl)
            {
                foreach (VariableDeclaration variable in varDecl.Variables)
                {
                    int line = lineOffset + variable.Location.Start.Line - 1;
                    variables.Add(new VariableInfo(variable.Identifier.Name, line));
                }
            }
        }

        return new ParsedBlock(subs, functions, variables);
    }

    private static ParsedBlock ParseWithRegex(string source, int lineOffset)
    {
        string[] lines = source.Split('\n');

        var subs = new List<SubInfo>();
        var functions = new List<FunctionInfo>();
        var variables = new List<VariableInfo>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int fileLine = lineOffset + i;

            Match subMatch = SubPattern.Match(line);
            if (subMatch.Success)
            {
                string name = subMatch.Groups[1].Value;
                IReadOnlyList<string> parameters = ParseParameters(subMatch.Groups[2].Value);
                int lineEnd = FindEndLine(lines, i, EndSubPattern, lineOffset);
                subs.Add(new SubInfo(name, parameters, fileLine, lineEnd));
                continue;
            }

            Match funcMatch = FunctionPattern.Match(line);
            if (funcMatch.Success)
            {
                string name = funcMatch.Groups[1].Value;
                IReadOnlyList<string> parameters = ParseParameters(funcMatch.Groups[2].Value);
                int lineEnd = FindEndLine(lines, i, EndFunctionPattern, lineOffset);
                functions.Add(new FunctionInfo(name, parameters, fileLine, lineEnd));
                continue;
            }

            Match varMatch = VariablePattern.Match(line);
            if (varMatch.Success)
            {
                variables.Add(new VariableInfo(varMatch.Groups[1].Value, fileLine));
            }
        }

        return new ParsedBlock(subs, functions, variables);
    }

    private static IReadOnlyList<string> ParseParameters(string paramString)
    {
        if (string.IsNullOrWhiteSpace(paramString))
            return Array.Empty<string>();

        return [.. paramString.Split(',')
            .Select(p => p.Trim())
            .Select(p => p.StartsWith("ByRef ", StringComparison.OrdinalIgnoreCase)
                ? p.Substring(6).Trim()
                : p)
            .Select(p => p.StartsWith("ByVal ", StringComparison.OrdinalIgnoreCase)
                ? p.Substring(6).Trim()
                : p)
            .Where(p => p.Length > 0)];
    }

    private static int FindEndLine(string[] lines, int startIndex, Regex endPattern, int lineOffset)
    {
        for (int j = startIndex + 1; j < lines.Length; j++)
        {
            if (endPattern.IsMatch(lines[j]))
            {
                return lineOffset + j;
            }
        }

        return lineOffset + startIndex;
    }
}
