namespace VBScript.Parser.Ast.Expressions
{
    public class IndexOrCallExpression(Expression obj) : Expression
    {
        public List<Expression> Indexes { get; } = [];
        public Expression Object { get; } = obj ?? throw new ArgumentNullException(nameof(obj));
    }
}
