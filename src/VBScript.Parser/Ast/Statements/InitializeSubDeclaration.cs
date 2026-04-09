using System.Diagnostics;
using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    [DebuggerDisplay("Sub {Identifier.Name}")]
    public class InitializeSubDeclaration(MethodAccessModifier modifier, Statement body) : SubDeclaration(modifier, new Identifier(Name), body)
    {
        public static readonly string Name = "Class_Initialize";
    }
}
