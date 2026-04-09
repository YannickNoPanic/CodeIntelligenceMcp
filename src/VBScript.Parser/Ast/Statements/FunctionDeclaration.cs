using System.Diagnostics;
using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    [DebuggerDisplay("Function {Identifier.Name}")]
    public class FunctionDeclaration(MethodAccessModifier modifier, Identifier id, Statement body) : ProcedureDeclaration(modifier, id, body)
    {
    }
}
