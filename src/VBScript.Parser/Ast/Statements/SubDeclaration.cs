using System.Diagnostics;
using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    [DebuggerDisplay("Sub {Identifier.Name}")]
    public class SubDeclaration(MethodAccessModifier modifier, Identifier id, Statement body) : ProcedureDeclaration(modifier, id, body)
    {
    }
}
