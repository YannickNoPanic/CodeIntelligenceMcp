using System.Diagnostics;
using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    [DebuggerDisplay("Sub {Identifier.Name}")]
    public class TerminateSubDeclaration(MethodAccessModifier modifier, Statement body) : SubDeclaration(modifier, new Identifier("Class_Terminate"), body)
    {
        public static readonly string Name = "Class_Terminate";
    }
}
