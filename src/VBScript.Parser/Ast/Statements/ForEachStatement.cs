using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class ForEachStatement(Identifier id, Expression @in) : Statement
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public Expression In { get; } = @in ?? throw new ArgumentNullException(nameof(@in));
        public List<Statement> Body { get; } = [];
    }
}
