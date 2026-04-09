using System.Management.Automation.Language;
using CodeIntelligenceMcp.PowerShell.Models;

namespace CodeIntelligenceMcp.PowerShell;

public static class PowerShellManifestParser
{
    public static PowerShellModuleManifest? Parse(string filePath, string content, Action<string>? log = null)
    {
        ScriptBlockAst ast = Parser.ParseInput(content, out _, out ParseError[] errors);

        if (errors.Length > 0)
        {
            log?.Invoke($"[warn] Manifest parse errors in {filePath}: {string.Join(", ", errors.Select(e => e.Message))}");
        }

        // .psd1 files consist of a single hashtable expression as the script body
        HashtableAst? root = FindRootHashtable(ast);
        if (root is null)
        {
            log?.Invoke($"[warn] No root hashtable found in manifest: {filePath}");
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(filePath);
        string? version = GetStringValue(root, "ModuleVersion");
        string? description = GetStringValue(root, "Description");
        IReadOnlyList<string> requiredModules = GetStringListOrModuleNames(root, "RequiredModules");
        IReadOnlyList<string> exportedFunctions = GetStringList(root, "FunctionsToExport");
        IReadOnlyList<string> exportedCmdlets = GetStringList(root, "CmdletsToExport");

        return new PowerShellModuleManifest(
            Name: name,
            Version: version,
            ManifestPath: filePath,
            RequiredModules: requiredModules,
            ExportedFunctions: exportedFunctions,
            ExportedCmdlets: exportedCmdlets,
            Description: description);
    }

    private static HashtableAst? FindRootHashtable(ScriptBlockAst ast)
    {
        // The script body should be a single statement that is a hashtable
        StatementAst? firstStatement = ast.EndBlock?.Statements.FirstOrDefault();

        return firstStatement switch
        {
            PipelineAst pipe when pipe.PipelineElements.Count == 1 &&
                pipe.PipelineElements[0] is CommandExpressionAst cmdExpr &&
                cmdExpr.Expression is HashtableAst ht => ht,
            _ => ast.FindAll(a => a is HashtableAst, false).Cast<HashtableAst>().FirstOrDefault()
        };
    }

    private static string? GetStringValue(HashtableAst hashtable, string key)
    {
        foreach ((ExpressionAst keyExpr, StatementAst valueStmt) in hashtable.KeyValuePairs)
        {
            if (!IsKeyMatch(keyExpr, key))
                continue;

            return ExtractStringFromStatement(valueStmt);
        }

        return null;
    }

    private static IReadOnlyList<string> GetStringList(HashtableAst hashtable, string key)
    {
        foreach ((ExpressionAst keyExpr, StatementAst valueStmt) in hashtable.KeyValuePairs)
        {
            if (!IsKeyMatch(keyExpr, key))
                continue;

            return ExtractStringList(valueStmt);
        }

        return [];
    }

    private static IReadOnlyList<string> GetStringListOrModuleNames(HashtableAst hashtable, string key)
    {
        foreach ((ExpressionAst keyExpr, StatementAst valueStmt) in hashtable.KeyValuePairs)
        {
            if (!IsKeyMatch(keyExpr, key))
                continue;

            // RequiredModules can be strings or @{ModuleName='...' ...} hashtables
            return ExtractModuleNames(valueStmt);
        }

        return [];
    }

    private static bool IsKeyMatch(ExpressionAst keyExpr, string expected)
    {
        string? keyStr = keyExpr switch
        {
            StringConstantExpressionAst s => s.Value,
            ConstantExpressionAst c => c.Value?.ToString(),
            _ => null
        };

        return string.Equals(keyStr, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractStringFromStatement(StatementAst stmt)
    {
        ExpressionAst? expr = stmt switch
        {
            PipelineAst pipe when pipe.PipelineElements.Count == 1 &&
                pipe.PipelineElements[0] is CommandExpressionAst cmdExpr => cmdExpr.Expression,
            _ => null
        };

        return expr switch
        {
            StringConstantExpressionAst s => s.Value,
            ConstantExpressionAst c => c.Value?.ToString(),
            _ => null
        };
    }

    private static IReadOnlyList<string> ExtractStringList(StatementAst stmt)
    {
        ExpressionAst? expr = UnwrapPipeline(stmt);
        if (expr is null)
            return [];

        IEnumerable<StringConstantExpressionAst> strings = expr switch
        {
            ArrayExpressionAst arrExpr => arrExpr
                .FindAll(a => a is StringConstantExpressionAst, false)
                .Cast<StringConstantExpressionAst>(),
            ArrayLiteralAst arr => arr.Elements.OfType<StringConstantExpressionAst>(),
            StringConstantExpressionAst s => [s],
            _ => []
        };

        return strings
            .Select(s => s.Value)
            .Where(s => !string.IsNullOrEmpty(s) && s != "*")
            .ToList();
    }

    private static IReadOnlyList<string> ExtractModuleNames(StatementAst stmt)
    {
        ExpressionAst? expr = UnwrapPipeline(stmt);
        if (expr is null)
            return [];

        var result = new List<string>();

        IEnumerable<ExpressionAst> elements = expr switch
        {
            ArrayExpressionAst arrExpr => arrExpr.SubExpression
                .FindAll(a => a is StringConstantExpressionAst or HashtableAst, false)
                .Cast<ExpressionAst>(),
            ArrayLiteralAst arr => arr.Elements,
            _ => [expr]
        };

        foreach (ExpressionAst element in elements)
        {
            switch (element)
            {
                case StringConstantExpressionAst s when !string.IsNullOrEmpty(s.Value):
                    result.Add(s.Value);
                    break;

                case HashtableAst ht:
                    // @{ModuleName='...'; ModuleVersion='...'}
                    string? modName = GetStringValue(ht, "ModuleName");
                    if (modName is not null)
                        result.Add(modName);
                    break;
            }
        }

        return result;
    }

    private static ExpressionAst? UnwrapPipeline(StatementAst stmt)
    {
        return stmt switch
        {
            PipelineAst pipe when pipe.PipelineElements.Count == 1 &&
                pipe.PipelineElements[0] is CommandExpressionAst cmdExpr => cmdExpr.Expression,
            _ => null
        };
    }
}
