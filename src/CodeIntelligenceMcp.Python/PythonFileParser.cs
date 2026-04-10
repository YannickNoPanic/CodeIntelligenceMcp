using System.Text;
using System.Text.RegularExpressions;
using CodeIntelligenceMcp.Python.Models;

namespace CodeIntelligenceMcp.Python;

public static class PythonFileParser
{
    private static readonly Regex FunctionDef =
        new(@"^(async\s+)?def\s+(\w+)\s*\(", RegexOptions.Compiled);

    private static readonly Regex ClassDef =
        new(@"^class\s+(\w+)\s*(?:\(([^)]*)\))?:", RegexOptions.Compiled);

    private static readonly Regex ImportSimple =
        new(@"^import\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex ImportFrom =
        new(@"^from\s+([.\w]+)\s+import\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex ReturnHint =
        new(@"->\s*([\w\[\], |.?]+)\s*:", RegexOptions.Compiled);

    private static readonly Regex AllNamePattern =
        new(@"['""](\w+)['""]", RegexOptions.Compiled);

    public static PythonFileInfo Parse(string filePath, string content)
    {
        string[] lines = content.Split('\n');
        var topFunctions = new List<PythonFunctionInfo>();
        var classBuilders = new List<ClassBuilder>();
        var classStack = new Stack<ClassBuilder>();
        var imports = new List<PythonImportInfo>();
        var exportedNames = new List<string>();
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDecorators = new List<string>();

        int i = 0;
        while (i < lines.Length)
        {
            string rawLine = lines[i];
            string trimmed = rawLine.TrimStart();
            int indent = rawLine.Length - trimmed.Length;

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                i++;
                continue;
            }

            // Finalize classes whose scope has ended
            while (classStack.Count > 0 && indent <= classStack.Peek().IndentLevel)
                classBuilders.Add(classStack.Pop());

            // Decorator
            if (trimmed.StartsWith('@'))
            {
                string dec = trimmed[1..];
                int spaceHash = dec.IndexOf(" #", StringComparison.Ordinal);
                if (spaceHash >= 0)
                    dec = dec[..spaceHash];
                pendingDecorators.Add(dec.TrimEnd());
                i++;
                continue;
            }

            // import x
            Match simpleMatch = ImportSimple.Match(trimmed);
            if (simpleMatch.Success && indent == 0)
            {
                foreach (string part in simpleMatch.Groups[1].Value.Split(','))
                {
                    string mod = part.Trim().Split(' ')[0].Trim();
                    if (!string.IsNullOrEmpty(mod))
                        imports.Add(new PythonImportInfo(mod, [], false, i + 1));
                }
                AddFrameworks(trimmed, frameworks);
                i++;
                continue;
            }

            // from x import y, z
            Match fromMatch = ImportFrom.Match(trimmed);
            if (fromMatch.Success && indent == 0)
            {
                string module = fromMatch.Groups[1].Value.Trim();
                bool isRelative = module.StartsWith('.');
                string namesStr = fromMatch.Groups[2].Value.Trim();

                IReadOnlyList<string> names = namesStr == "*"
                    ? (IReadOnlyList<string>)["*"]
                    : namesStr.Split(',')
                        .Select(n => n.Trim().Split(' ')[0].Trim())
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();

                imports.Add(new PythonImportInfo(module, names, isRelative, i + 1));
                AddFrameworks(module, frameworks);
                i++;
                continue;
            }

            // __all__
            if (indent == 0 && trimmed.StartsWith("__all__", StringComparison.Ordinal))
            {
                List<string> exports = ExtractAll(lines, i, out int consumed);
                exportedNames.AddRange(exports);
                i += consumed;
                continue;
            }

            // class Foo(Bar):
            Match classMatch = ClassDef.Match(trimmed);
            if (classMatch.Success)
            {
                string name = classMatch.Groups[1].Value;
                string basesStr = classMatch.Groups[2].Success ? classMatch.Groups[2].Value : string.Empty;
                var builder = new ClassBuilder(name, i + 1, indent, SplitBases(basesStr), [..pendingDecorators]);
                pendingDecorators.Clear();
                classStack.Push(builder);
                i++;
                continue;
            }

            // def foo(...):
            Match funcMatch = FunctionDef.Match(trimmed);
            if (funcMatch.Success)
            {
                bool isAsync = funcMatch.Groups[1].Success;
                string name = funcMatch.Groups[2].Value;

                (string paramStr, int endLine) = ScanToCloseParen(lines, i);
                string? returnType = ExtractReturn(lines, endLine);
                IReadOnlyList<PythonParameterInfo> parameters = ParseParameters(paramStr);
                bool isMethod = classStack.Count > 0 && indent > classStack.Peek().IndentLevel;

                var func = new PythonFunctionInfo(
                    name, i + 1, endLine + 1, parameters, returnType,
                    [..pendingDecorators], isAsync, isMethod);

                pendingDecorators.Clear();

                if (isMethod)
                    classStack.Peek().Methods.Add(func);
                else
                    topFunctions.Add(func);

                i = endLine + 1;
                continue;
            }

            pendingDecorators.Clear();
            i++;
        }

        while (classStack.Count > 0)
            classBuilders.Add(classStack.Pop());

        return new PythonFileInfo(
            FilePath: filePath,
            Functions: topFunctions,
            Classes: classBuilders.Select(b => b.Build()).ToList(),
            Imports: imports,
            ExportedNames: exportedNames,
            DetectedFrameworks: [..frameworks]);
    }

    private static (string ParamStr, int EndLine) ScanToCloseParen(string[] lines, int startLine)
    {
        string startTrimmed = lines[startLine].TrimStart();
        int openParen = startTrimmed.IndexOf('(');
        if (openParen < 0)
            return (string.Empty, startLine);

        var sb = new StringBuilder();
        int depth = 0;
        int lineIdx = startLine;
        bool inString = false;
        char stringChar = '"';

        while (lineIdx < lines.Length)
        {
            string line = lineIdx == startLine ? startTrimmed : lines[lineIdx];
            int startIdx = lineIdx == startLine ? openParen : 0;

            for (int ci = startIdx; ci < line.Length; ci++)
            {
                char c = line[ci];

                if (inString)
                {
                    if (c == stringChar && (ci == 0 || line[ci - 1] != '\\'))
                        inString = false;
                    if (depth > 0) sb.Append(c);
                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = true;
                    stringChar = c;
                    if (depth > 0) sb.Append(c);
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                    if (depth > 1) sb.Append(c);
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                        return (sb.ToString(), lineIdx);
                    sb.Append(c);
                }
                else if (depth > 0)
                {
                    sb.Append(c);
                }
            }

            if (depth > 0)
                sb.Append(' '); // normalize newline in multi-line signature

            lineIdx++;
        }

        return (sb.ToString(), Math.Min(lineIdx - 1, lines.Length - 1));
    }

    private static string? ExtractReturn(string[] lines, int endLine)
    {
        int start = Math.Max(0, endLine - 1);
        int end = Math.Min(endLine + 1, lines.Length - 1);
        for (int l = start; l <= end; l++)
        {
            Match m = ReturnHint.Match(lines[l]);
            if (m.Success)
                return m.Groups[1].Value.Trim();
        }
        return null;
    }

    private static IReadOnlyList<PythonParameterInfo> ParseParameters(string paramStr)
    {
        if (string.IsNullOrWhiteSpace(paramStr))
            return [];

        var result = new List<PythonParameterInfo>();

        foreach (string part in SplitAtTopLevelCommas(paramStr))
        {
            string p = part.Trim();
            if (string.IsNullOrEmpty(p) || p == "/" || p == "*")
                continue;

            string clean = p.TrimStart('*');

            // Find default value separator (= not inside brackets)
            string nameAndType = clean;
            string? defaultValue = null;
            int eqIdx = FindTopLevelChar(clean, '=');
            if (eqIdx >= 0)
            {
                defaultValue = clean[(eqIdx + 1)..].Trim();
                nameAndType = clean[..eqIdx].Trim();
            }

            // Split name from type hint at first colon
            string paramName;
            string? typeHint = null;
            int colonIdx = FindTopLevelChar(nameAndType, ':');
            if (colonIdx >= 0)
            {
                paramName = nameAndType[..colonIdx].Trim();
                typeHint = nameAndType[(colonIdx + 1)..].Trim();
            }
            else
            {
                paramName = nameAndType.Trim();
            }

            if (!string.IsNullOrEmpty(paramName))
                result.Add(new PythonParameterInfo(paramName, typeHint, defaultValue));
        }

        return result;
    }

    private static int FindTopLevelChar(string s, char target)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '[' or '{' or '(') depth++;
            else if (c is ']' or '}' or ')') depth--;
            else if (c == target && depth == 0) return i;
        }
        return -1;
    }

    private static List<string> SplitAtTopLevelCommas(string s)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;

        foreach (char c in s)
        {
            if (c is '[' or '{' or '(') { depth++; current.Append(c); }
            else if (c is ']' or '}' or ')') { depth--; current.Append(c); }
            else if (c == ',' && depth == 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    private static List<string> ExtractAll(string[] lines, int startLine, out int consumed)
    {
        var result = new List<string>();
        string line = lines[startLine];
        consumed = 1;

        int openBracket = line.IndexOf('[');
        if (openBracket < 0)
            return result;

        int closeBracket = line.IndexOf(']', openBracket);
        if (closeBracket > openBracket)
        {
            ExtractAllNames(line[(openBracket + 1)..closeBracket], result);
            return result;
        }

        // Multi-line __all__
        var sb = new StringBuilder(line[(openBracket + 1)..]);
        int l = startLine + 1;
        while (l < lines.Length)
        {
            string nextLine = lines[l];
            int close = nextLine.IndexOf(']');
            if (close >= 0)
            {
                sb.Append(nextLine[..close]);
                consumed = l - startLine + 1;
                break;
            }
            sb.Append(nextLine);
            l++;
        }

        ExtractAllNames(sb.ToString(), result);
        return result;
    }

    private static void ExtractAllNames(string content, List<string> result)
    {
        foreach (Match m in AllNamePattern.Matches(content))
            result.Add(m.Groups[1].Value);
    }

    private static IReadOnlyList<string> SplitBases(string basesStr)
    {
        if (string.IsNullOrWhiteSpace(basesStr))
            return [];
        return basesStr.Split(',')
            .Select(b => b.Trim().Split('(')[0].Trim()) // handle metaclass= args
            .Where(b => !string.IsNullOrEmpty(b) && !b.Contains('='))
            .ToList();
    }

    private static void AddFrameworks(string line, HashSet<string> frameworks)
    {
        if (line.Contains("fastapi", StringComparison.OrdinalIgnoreCase)) frameworks.Add("FastAPI");
        if (line.Contains("flask", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Flask");
        if (line.Contains("django", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Django");
        if (line.Contains("pydantic", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Pydantic");
        if (line.Contains("sqlalchemy", StringComparison.OrdinalIgnoreCase)) frameworks.Add("SQLAlchemy");
        if (line.Contains("pytest", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Pytest");
        if (line.Contains("aiohttp", StringComparison.OrdinalIgnoreCase)) frameworks.Add("aiohttp");
        if (line.Contains("starlette", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Starlette");
        if (line.Contains("celery", StringComparison.OrdinalIgnoreCase)) frameworks.Add("Celery");
        if (line.Contains("pandas", StringComparison.OrdinalIgnoreCase)) frameworks.Add("pandas");
        if (line.Contains("numpy", StringComparison.OrdinalIgnoreCase)) frameworks.Add("numpy");
    }

    private sealed class ClassBuilder(
        string name,
        int lineStart,
        int indentLevel,
        IReadOnlyList<string> baseClasses,
        IReadOnlyList<string> decorators)
    {
        public string Name { get; } = name;
        public int LineStart { get; } = lineStart;
        public int IndentLevel { get; } = indentLevel;
        public IReadOnlyList<string> BaseClasses { get; } = baseClasses;
        public IReadOnlyList<string> Decorators { get; } = decorators;
        public List<PythonFunctionInfo> Methods { get; } = [];

        public PythonClassInfo Build() =>
            new(Name, LineStart, 0, BaseClasses, Methods, Decorators);
    }
}
