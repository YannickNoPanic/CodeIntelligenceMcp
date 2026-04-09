using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast
{
    public class ConstDeclaration(Identifier id, Expression init) : Node
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public Expression Init { get; } = init ?? throw new ArgumentNullException(nameof(init));
    }
}
