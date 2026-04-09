using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast
{
    public class ReDimDeclaration(Identifier id) : Node
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public List<Expression> ArrayDims { get; } = [];
    }
}
