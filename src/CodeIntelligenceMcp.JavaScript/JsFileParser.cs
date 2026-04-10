using System.Text;
using System.Text.RegularExpressions;
using CodeIntelligenceMcp.JavaScript.Models;

namespace CodeIntelligenceMcp.JavaScript;

public static class JsFileParser
{
    // Function declarations: export? async? function *? name(
    private static readonly Regex FunctionDecl =
        new(@"^(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*(\*)?\s*(\w+)\s*(?:<[^>]*>)?\s*\(",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // Arrow functions: export? const/let/var name = async? (
    private static readonly Regex ArrowFunction =
        new(@"^(?:export\s+)?(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[\w]+)\s*=>",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // Arrow function with explicit params: export? const name = async? (
    private static readonly Regex ArrowFunctionParen =
        new(@"^(?:export\s+)?(?:const|let|var)\s+(\w+)\s*=\s*(async\s+)?\(",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // Class: export? abstract? class Name extends Base
    private static readonly Regex ClassDecl =
        new(@"^(?:export\s+)?(?:default\s+)?(?:abstract\s+)?class\s+(\w+)(?:\s+extends\s+([\w.]+))?",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // TypeScript interface
    private static readonly Regex InterfaceDecl =
        new(@"^(?:export\s+)?interface\s+(\w+)(?:\s+extends\s+([\w,\s<>]+?))?(?:\s*\{|$)",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // TypeScript type alias
    private static readonly Regex TypeAlias =
        new(@"^(?:export\s+)?type\s+(\w+)\s*(?:<[^>]*>)?\s*=",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // TypeScript enum
    private static readonly Regex EnumDecl =
        new(@"^(?:export\s+)?(?:const\s+)?enum\s+(\w+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // ESM imports
    private static readonly Regex EsmImport =
        new(@"^import\s+(type\s+)?(?:\*\s+as\s+(\w+)|\{([^}]+)\}|(\w+)(?:\s*,\s*\{([^}]+)\})?)\s+from\s+['""]([^'""]+)['""]",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // CommonJS require
    private static readonly Regex CjsRequire =
        new(@"(?:const|let|var)\s+(?:\{([^}]+)\}|(\w+))\s*=\s*require\s*\(\s*['""]([^'""]+)['""]\s*\)",
            RegexOptions.Compiled);

    // Named exports: export { foo, bar }
    private static readonly Regex NamedExport =
        new(@"^export\s+\{([^}]+)\}", RegexOptions.Compiled | RegexOptions.Multiline);

    // Re-exports: export { foo } from 'bar' or export * from 'bar'
    private static readonly Regex ReExport =
        new(@"^export\s+(?:\{[^}]+\}|\*)\s+from\s+['""][^'""]+['""]",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // export default
    private static readonly Regex DefaultExport =
        new(@"^export\s+default\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    public static JsFileInfo Parse(string filePath, string content, int lineOffset = 0)
    {
        string[] lines = content.Split('\n');
        var functions = new List<JsFunctionInfo>();
        var classes = new List<JsClassInfo>();
        var imports = new List<JsImportInfo>();
        var exports = new List<JsExportInfo>();
        var interfaces = new List<JsInterfaceInfo>();
        var typeAliases = new List<JsTypeAliasInfo>();
        var enums = new List<JsEnumInfo>();
        bool hasEsm = false;
        bool hasCjs = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string rawLine = lines[i];
            string trimmed = rawLine.TrimStart();

            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal))
                continue;

            int lineNum = i + 1 + lineOffset;

            // ESM import
            Match esmMatch = EsmImport.Match(trimmed);
            if (esmMatch.Success)
            {
                hasEsm = true;
                bool isTypeOnly = esmMatch.Groups[1].Success;
                string? nsImport = esmMatch.Groups[2].Success ? esmMatch.Groups[2].Value : null;
                string namedStr = esmMatch.Groups[3].Success ? esmMatch.Groups[3].Value : string.Empty;
                string? defaultImport = esmMatch.Groups[4].Success ? esmMatch.Groups[4].Value : null;
                string namedStr2 = esmMatch.Groups[5].Success ? esmMatch.Groups[5].Value : string.Empty;
                string source = esmMatch.Groups[6].Value;

                IReadOnlyList<string> named = ParseNamedImports(namedStr + "," + namedStr2);
                imports.Add(new JsImportInfo(source, named, defaultImport, nsImport, isTypeOnly, lineNum));
                continue;
            }

            // CJS require
            Match cjsMatch = CjsRequire.Match(trimmed);
            if (cjsMatch.Success)
            {
                hasCjs = true;
                string namedStr = cjsMatch.Groups[1].Value;
                string? defaultImport = cjsMatch.Groups[2].Success ? cjsMatch.Groups[2].Value : null;
                string source = cjsMatch.Groups[3].Value;
                IReadOnlyList<string> named = ParseNamedImports(namedStr);
                imports.Add(new JsImportInfo(source, named, defaultImport, null, false, lineNum));
                continue;
            }

            // Re-export (check before named export)
            if (ReExport.IsMatch(trimmed))
            {
                exports.Add(new JsExportInfo("*", "re-export", false, lineNum));
                continue;
            }

            // Named exports: export { foo, bar as baz }
            Match namedExportMatch = NamedExport.Match(trimmed);
            if (namedExportMatch.Success)
            {
                foreach (string name in ParseNamedImports(namedExportMatch.Groups[1].Value))
                    exports.Add(new JsExportInfo(name, "named", false, lineNum));
                continue;
            }

            // TypeScript interface
            Match ifaceMatch = InterfaceDecl.Match(trimmed);
            if (ifaceMatch.Success)
            {
                string name = ifaceMatch.Groups[1].Value;
                bool isExported = trimmed.StartsWith("export", StringComparison.Ordinal);
                string extendsStr = ifaceMatch.Groups[2].Success ? ifaceMatch.Groups[2].Value : string.Empty;
                IReadOnlyList<string> extendsList = extendsStr.Split(',')
                    .Select(e => e.Trim().Split('<')[0].Trim())
                    .Where(e => !string.IsNullOrEmpty(e))
                    .ToList();
                int endLine = FindClosingBrace(lines, i);
                interfaces.Add(new JsInterfaceInfo(name, lineNum, endLine + lineOffset, extendsList, isExported));
                continue;
            }

            // TypeScript type alias
            Match typeMatch = TypeAlias.Match(trimmed);
            if (typeMatch.Success)
            {
                string name = typeMatch.Groups[1].Value;
                bool isExported = trimmed.StartsWith("export", StringComparison.Ordinal);
                typeAliases.Add(new JsTypeAliasInfo(name, lineNum, isExported));
                if (isExported) exports.Add(new JsExportInfo(name, "type", true, lineNum));
                continue;
            }

            // TypeScript enum
            Match enumMatch = EnumDecl.Match(trimmed);
            if (enumMatch.Success)
            {
                string name = enumMatch.Groups[1].Value;
                bool isExported = trimmed.StartsWith("export", StringComparison.Ordinal);
                bool isConst = trimmed.Contains("const enum", StringComparison.Ordinal);
                int endLine = FindClosingBrace(lines, i);
                enums.Add(new JsEnumInfo(name, lineNum, endLine + lineOffset, isExported, isConst));
                if (isExported) exports.Add(new JsExportInfo(name, "enum", false, lineNum));
                continue;
            }

            // Class declaration
            Match classMatch = ClassDecl.Match(trimmed);
            if (classMatch.Success && !trimmed.Contains("interface", StringComparison.Ordinal))
            {
                string name = classMatch.Groups[1].Value;
                string? extends = classMatch.Groups[2].Success ? classMatch.Groups[2].Value : null;
                bool isExported = trimmed.StartsWith("export", StringComparison.Ordinal);
                bool isAbstract = trimmed.Contains("abstract", StringComparison.Ordinal);
                int endLine = FindClosingBrace(lines, i);
                var methods = ExtractMethods(lines, i, endLine, lineOffset);
                classes.Add(new JsClassInfo(name, lineNum, endLine + lineOffset, extends, [], methods, isExported, isAbstract));
                if (isExported) exports.Add(new JsExportInfo(name, "class", false, lineNum));
                continue;
            }

            // Function declaration
            Match funcDeclMatch = FunctionDecl.Match(trimmed);
            if (funcDeclMatch.Success)
            {
                bool isGenerator = funcDeclMatch.Groups[1].Success;
                string name = funcDeclMatch.Groups[2].Value;
                bool isExported = trimmed.StartsWith("export", StringComparison.Ordinal);
                bool isAsync = trimmed.Contains("async ", StringComparison.Ordinal);
                int endLine = FindClosingBrace(lines, i);
                functions.Add(new JsFunctionInfo(name, lineNum, endLine + lineOffset, [], null, isExported, isAsync, isGenerator, "declaration"));
                if (isExported) exports.Add(new JsExportInfo(name, "function", false, lineNum));
                continue;
            }

            // Arrow function
            Match arrowMatch = ArrowFunctionParen.Match(trimmed);
            if (arrowMatch.Success)
            {
                string name = arrowMatch.Groups[1].Value;
                bool isAsync = arrowMatch.Groups[2].Success;
                bool isExported = trimmed.StartsWith("export", StringComparison.Ordinal);
                int endLine = FindArrowEnd(lines, i);
                functions.Add(new JsFunctionInfo(name, lineNum, endLine + lineOffset, [], null, isExported, isAsync, false, "arrow"));
                if (isExported) exports.Add(new JsExportInfo(name, "const", false, lineNum));
                continue;
            }

            // export default
            if (DefaultExport.IsMatch(trimmed))
            {
                exports.Add(new JsExportInfo("default", "default", false, lineNum));
            }
        }

        string moduleType = (hasEsm, hasCjs) switch
        {
            (true, true) => "mixed",
            (true, false) => "esm",
            (false, true) => "commonjs",
            _ => "none"
        };

        return new JsFileInfo(filePath, functions, classes, imports, exports, interfaces, typeAliases, enums, moduleType);
    }

    private static IReadOnlyList<string> ParseNamedImports(string namedStr)
    {
        if (string.IsNullOrWhiteSpace(namedStr))
            return [];

        return namedStr.Split(',')
            .Select(n =>
            {
                string trimmed = n.Trim();
                // Handle "foo as bar" → "foo"
                int asIdx = trimmed.IndexOf(" as ", StringComparison.Ordinal);
                return asIdx >= 0 ? trimmed[..asIdx].Trim() : trimmed;
            })
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    private static int FindClosingBrace(string[] lines, int startLine)
    {
        int depth = 0;
        bool started = false;
        for (int i = startLine; i < lines.Length; i++)
        {
            foreach (char c in lines[i])
            {
                if (c == '{') { depth++; started = true; }
                else if (c == '}' && started)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }
        return startLine;
    }

    private static int FindArrowEnd(string[] lines, int startLine)
    {
        string line = lines[startLine];
        // Single-line arrow: const foo = () => expression
        if (line.Contains("=>") && !line.TrimEnd().EndsWith('{'))
            return startLine;
        return FindClosingBrace(lines, startLine);
    }

    private static IReadOnlyList<JsFunctionInfo> ExtractMethods(
        string[] lines, int classStart, int classEnd, int lineOffset)
    {
        var methods = new List<JsFunctionInfo>();

        // Simple method pattern: optional async, name, (
        var methodPattern = new Regex(
            @"^\s+(?:(async)\s+)?(?:(?:public|private|protected|static|override|readonly|abstract)\s+)*(\w+)\s*\(",
            RegexOptions.Compiled);

        var skipKeywords = new HashSet<string>(StringComparer.Ordinal)
            { "if", "for", "while", "switch", "catch", "constructor", "super" };

        for (int i = classStart + 1; i < classEnd && i < lines.Length; i++)
        {
            Match m = methodPattern.Match(lines[i]);
            if (!m.Success) continue;

            string name = m.Groups[2].Value;
            if (skipKeywords.Contains(name)) continue;

            bool isAsync = m.Groups[1].Success;
            int lineNum = i + 1 + lineOffset;
            int endLine = FindClosingBrace(lines, i);

            methods.Add(new JsFunctionInfo(name, lineNum, endLine + lineOffset, [], null, false, isAsync, false, "method"));
        }

        return methods;
    }
}
