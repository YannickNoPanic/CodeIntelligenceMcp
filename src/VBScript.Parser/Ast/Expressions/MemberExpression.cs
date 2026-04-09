namespace VBScript.Parser.Ast.Expressions
{
    public class MemberExpression(Expression obj, Identifier property) : Expression
    {
        public Expression Object { get; } = obj ?? throw new ArgumentNullException(nameof(obj));
        public Identifier Property { get; } = property ?? throw new ArgumentNullException(nameof(property));
    }
}
