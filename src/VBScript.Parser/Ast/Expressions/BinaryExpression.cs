namespace VBScript.Parser.Ast.Expressions
{
    public class BinaryExpression(BinaryOperation op, Expression left, Expression right) : Expression
    {
        public BinaryOperation Operation { get; } = op;
        public Expression Left { get; } = left;
        public Expression Right { get; } = right;
    }

    public enum BinaryOperation
    {
        Exponentiation,
        Multiplication,
        Division,
        IntDivision,
        Addition,
        Subtraction,
        Concatenation,
        Mod,
        Is,
        And,
        Or,
        Xor,
        Eqv,
        Imp,
        Equal,
        NotEqual,
        Less,
        Greater,
        LessOrEqual,
        GreaterOrEqual,
    }
}
