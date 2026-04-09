using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class AssignmentStatement(Expression left, Expression right, bool set) : Statement
    {
        public bool Set { get; } = set;
        public Expression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
        public Expression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));
    }
}
