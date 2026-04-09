using System.Diagnostics;
using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast
{
    [DebuggerDisplay("{Identifier.Name}")]
    public class VariableDeclaration(Identifier id, bool isDynamicArray) : VariableDeclarationNode(id, isDynamicArray)
    {
    }
}
