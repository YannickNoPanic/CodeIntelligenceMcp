namespace VBScript.Parser.Ast.Expressions
{
    public class NewExpression(Expression arg) : Expression
    {
        public Expression Argument { get; } = arg ?? throw new ArgumentNullException();
    }
}
