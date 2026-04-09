using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class CallStatement(Expression callee) : Statement
    {
        public Expression Callee { get; } = callee ?? throw new ArgumentNullException(nameof(callee));
    }
}
