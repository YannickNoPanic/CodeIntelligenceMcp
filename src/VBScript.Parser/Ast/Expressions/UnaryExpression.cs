namespace VBScript.Parser.Ast.Expressions
{
    public class UnaryExpression(UnaryOperation operation, Expression arg) : Expression
    {
        public UnaryOperation Operation { get; } = operation;
        public Expression Argument { get; } = arg ?? throw new ArgumentNullException(nameof(arg));
    }

    public enum UnaryOperation
    {
        Plus,
        Minus,
        Not,
    }
}
