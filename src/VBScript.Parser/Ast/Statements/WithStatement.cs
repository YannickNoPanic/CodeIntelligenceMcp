using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class WithStatement(Expression expr) : Statement
    {
        public Expression Expression { get; } = expr ?? throw new ArgumentNullException(nameof(expr));
        public List<Statement> Body { get; } = [];
    }
}
