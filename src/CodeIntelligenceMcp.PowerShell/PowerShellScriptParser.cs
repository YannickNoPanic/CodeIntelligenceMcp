using System.Management.Automation.Language;
using CodeIntelligenceMcp.PowerShell.Models;

namespace CodeIntelligenceMcp.PowerShell;

public static class PowerShellScriptParser
{
    private static readonly HashSet<string> KnownVerbPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Add", "Clear", "Close", "Copy", "Enter", "Exit", "Find", "Format", "Get", "Hide",
        "Join", "Lock", "Move", "New", "Open", "Optimize", "Pop", "Push", "Redo", "Remove",
        "Rename", "Reset", "Resize", "Search", "Select", "Set", "Show", "Skip", "Split",
        "Step", "Switch", "Undo", "Unlock", "Watch", "Connect", "Disconnect", "Read", "Receive",
        "Send", "Write", "Backup", "Checkpoint", "Compare", "Compress", "Convert", "ConvertFrom",
        "ConvertTo", "Dismount", "Edit", "Expand", "Export", "Group", "Import", "Initialize",
        "Limit", "Merge", "Mount", "Out", "Publish", "Restore", "Save", "Sync", "Unpublish",
        "Update", "Approve", "Assert", "Build", "Complete", "Confirm", "Deny", "Deploy",
        "Disable", "Enable", "Install", "Invoke", "Register", "Request", "Restart", "Resume",
        "Start", "Stop", "Submit", "Suspend", "Uninstall", "Unregister", "Wait", "Debug",
        "Measure", "Ping", "Repair", "Resolve", "Test", "Trace", "Block", "Grant", "Protect",
        "Revoke", "Unblock", "Unprotect", "Use"
    };

    public static PowerShellFileInfo Parse(string filePath, string content)
    {
        ScriptBlockAst ast = Parser.ParseInput(
            content,
            out _,
            out ParseError[] parseErrors);

        if (parseErrors.Length > 0)
        {
            // Log parse errors silently — still process what we got
        }

        var functions = ExtractFunctions(ast);
        var imports = ExtractImports(ast, content);
        var variables = ExtractScriptVariables(ast);
        var cmdlets = ExtractCmdletUsages(ast, functions);

        return new PowerShellFileInfo(filePath, functions, imports, variables, cmdlets);
    }

    private static IReadOnlyList<PowerShellFunctionInfo> ExtractFunctions(ScriptBlockAst ast)
    {
        var result = new List<PowerShellFunctionInfo>();

        IEnumerable<Ast> functionAsts = ast.FindAll(a => a is FunctionDefinitionAst, false);

        foreach (FunctionDefinitionAst funcAst in functionAsts.Cast<FunctionDefinitionAst>())
        {
            var parameters = ExtractParameters(funcAst);
            bool hasCmdletBinding = HasCmdletBinding(funcAst);
            bool supportsPipeline = parameters.Any(p => p.IsFromPipeline);
            bool hasTryCatch = funcAst.FindAll(a => a is TryStatementAst, true).Any();

            result.Add(new PowerShellFunctionInfo(
                Name: funcAst.Name,
                Parameters: parameters,
                LineStart: funcAst.Extent.StartLineNumber,
                LineEnd: funcAst.Extent.EndLineNumber,
                HasCmdletBinding: hasCmdletBinding,
                SupportsPipeline: supportsPipeline,
                HasTryCatch: hasTryCatch));
        }

        return result;
    }

    private static IReadOnlyList<PowerShellParameterInfo> ExtractParameters(FunctionDefinitionAst funcAst)
    {
        ParamBlockAst? paramBlock = funcAst.Body.ParamBlock;
        if (paramBlock is null)
            return [];

        var result = new List<PowerShellParameterInfo>();

        foreach (ParameterAst param in paramBlock.Parameters)
        {
            string name = param.Name.VariablePath.UserPath;
            string? type = param.StaticType?.Name;
            bool isMandatory = false;
            bool isFromPipeline = false;
            string? defaultValue = param.DefaultValue?.ToString();

            foreach (AttributeBaseAst attr in param.Attributes)
            {
                if (attr is AttributeAst attrAst &&
                    string.Equals(attrAst.TypeName.Name, "Parameter", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (NamedAttributeArgumentAst namedArg in attrAst.NamedArguments)
                    {
                        if (string.Equals(namedArg.ArgumentName, "Mandatory", StringComparison.OrdinalIgnoreCase))
                            isMandatory = IsTrueValue(namedArg.Argument);

                        if (string.Equals(namedArg.ArgumentName, "ValueFromPipeline", StringComparison.OrdinalIgnoreCase))
                            isFromPipeline = IsTrueValue(namedArg.Argument);

                        if (string.Equals(namedArg.ArgumentName, "ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))
                            isFromPipeline = isFromPipeline || IsTrueValue(namedArg.Argument);
                    }
                }
            }

            result.Add(new PowerShellParameterInfo(name, type, isMandatory, isFromPipeline, defaultValue));
        }

        return result;
    }

    private static bool HasCmdletBinding(FunctionDefinitionAst funcAst)
    {
        ParamBlockAst? paramBlock = funcAst.Body.ParamBlock;
        if (paramBlock is null)
            return false;

        return paramBlock.Attributes.Any(a =>
            string.Equals(a.TypeName.Name, "CmdletBinding", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTrueValue(ExpressionAst expr)
    {
        if (expr is ConstantExpressionAst constant)
            return constant.Value is true;
        if (expr is VariableExpressionAst variable)
            return string.Equals(variable.VariablePath.UserPath, "true", StringComparison.OrdinalIgnoreCase);
        return true; // bare [Parameter(Mandatory)] without value defaults to $true
    }

    private static IReadOnlyList<ImportedModule> ExtractImports(ScriptBlockAst ast, string content)
    {
        var result = new List<ImportedModule>();

        // Import-Module calls via AST
        foreach (CommandAst cmdAst in ast.FindAll(a => a is CommandAst, true).Cast<CommandAst>())
        {
            if (cmdAst.CommandElements.Count < 2)
                continue;

            string cmdName = cmdAst.CommandElements[0].ToString();
            if (!string.Equals(cmdName, "Import-Module", StringComparison.OrdinalIgnoreCase))
                continue;

            // Find the module name — first positional arg or -Name argument
            string? moduleName = null;
            string? version = null;

            for (int i = 1; i < cmdAst.CommandElements.Count; i++)
            {
                CommandElementAst elem = cmdAst.CommandElements[i];

                if (elem is CommandParameterAst param)
                {
                    if (string.Equals(param.ParameterName, "Name", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < cmdAst.CommandElements.Count)
                    {
                        moduleName = cmdAst.CommandElements[i + 1].ToString().Trim('"', '\'');
                        i++;
                    }
                    else if (string.Equals(param.ParameterName, "RequiredVersion", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(param.ParameterName, "MinimumVersion", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 < cmdAst.CommandElements.Count)
                        {
                            version = cmdAst.CommandElements[i + 1].ToString().Trim('"', '\'');
                            i++;
                        }
                    }
                }
                else if (moduleName is null && elem is not CommandParameterAst)
                {
                    string val = elem.ToString().Trim('"', '\'');
                    if (!val.StartsWith('-'))
                        moduleName = val;
                }
            }

            if (moduleName is not null)
            {
                result.Add(new ImportedModule(moduleName, version, cmdAst.Extent.StartLineNumber));
            }
        }

        // #Requires -Module via line scan (these are comments, not in the AST command tree)
        string[] lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("#Requires", StringComparison.OrdinalIgnoreCase))
                continue;

            int moduleIdx = line.IndexOf("-Module", StringComparison.OrdinalIgnoreCase);
            if (moduleIdx < 0)
                continue;

            string remainder = line[(moduleIdx + 7)..].Trim();
            // Can be a module name or @{ModuleName='...' MinimumVersion='...'} hashtable
            if (remainder.StartsWith('@'))
            {
                // Hashtable form: extract ModuleName
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(remainder,
                        @"ModuleName\s*=\s*['""]([^'""]+)['""]",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                    result.Add(new ImportedModule(m.Groups[1].Value, null, i + 1));
            }
            else
            {
                string moduleName = remainder.Split(',')[0].Trim('"', '\'', ' ');
                if (!string.IsNullOrEmpty(moduleName))
                    result.Add(new ImportedModule(moduleName, null, i + 1));
            }
        }

        return result;
    }

    private static IReadOnlyList<ScriptVariable> ExtractScriptVariables(ScriptBlockAst ast)
    {
        // Only top-level assignments (not inside functions)
        var result = new List<ScriptVariable>();
        var functionExtents = ast
            .FindAll(a => a is FunctionDefinitionAst, false)
            .Select(a => a.Extent)
            .ToList();

        foreach (AssignmentStatementAst assignment in
            ast.FindAll(a => a is AssignmentStatementAst, false).Cast<AssignmentStatementAst>())
        {
            // Skip assignments inside function bodies
            bool insideFunction = functionExtents.Any(fe =>
                assignment.Extent.StartLineNumber >= fe.StartLineNumber &&
                assignment.Extent.EndLineNumber <= fe.EndLineNumber);

            if (insideFunction)
                continue;

            if (assignment.Left is VariableExpressionAst varExpr)
            {
                string name = varExpr.VariablePath.UserPath;
                result.Add(new ScriptVariable(name, assignment.Extent.StartLineNumber));
            }
        }

        return result;
    }

    private static IReadOnlyList<CmdletUsage> ExtractCmdletUsages(
        ScriptBlockAst ast,
        IReadOnlyList<PowerShellFunctionInfo> localFunctions)
    {
        var localNames = new HashSet<string>(
            localFunctions.Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (CommandAst cmdAst in ast.FindAll(a => a is CommandAst, true).Cast<CommandAst>())
        {
            if (cmdAst.CommandElements.Count == 0)
                continue;

            string name = cmdAst.CommandElements[0].ToString();

            // Only count Verb-Noun cmdlets, skip local functions, keywords, variables
            if (!IsCmdletName(name) || localNames.Contains(name))
                continue;

            counts[name] = counts.TryGetValue(name, out int existing) ? existing + 1 : 1;
        }

        return counts
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => new CmdletUsage(kvp.Key, kvp.Value))
            .ToList();
    }

    private static bool IsCmdletName(string name)
    {
        int hyphen = name.IndexOf('-');
        if (hyphen <= 0 || hyphen == name.Length - 1)
            return false;

        string verb = name[..hyphen];
        return KnownVerbPrefixes.Contains(verb);
    }
}
