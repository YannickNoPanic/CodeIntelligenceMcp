using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class WhileStatement(Expression condition) : Statement
    {
        public Expression Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));
        public List<Statement> Body { get; } = [];
    }
}
