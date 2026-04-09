using System.Diagnostics;
using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast
{
    [DebuggerDisplay("{Identifier.Name}")]
    public class FieldDeclaration(Identifier id, bool isDynamicArray) : VariableDeclarationNode(id, isDynamicArray)
    {
    }
}
