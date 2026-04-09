using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class ForStatement(Identifier id, Expression from, Expression to, Expression? step) : Statement
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public Expression From { get; } = from;
        public Expression To { get; } = to;
        public Expression? Step { get; } = step;
        public List<Statement> Body { get; } = [];
    }
}
