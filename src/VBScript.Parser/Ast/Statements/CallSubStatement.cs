using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class CallSubStatement(Expression callee) : Statement
    {
        public Expression Callee { get; } = callee ?? throw new ArgumentNullException(nameof(callee));
        public List<Expression> Arguments { get; } = [];
    }
}
