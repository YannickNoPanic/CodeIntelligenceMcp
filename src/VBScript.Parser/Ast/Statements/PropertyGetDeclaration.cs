using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class PropertyGetDeclaration(MethodAccessModifier modifier, Identifier id) : PropertyDeclaration(modifier, id)
    {
    }
}
