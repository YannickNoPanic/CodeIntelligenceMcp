using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay("{Value}")]
    public class FloatLiteral(double value) : LiteralExpression
    {
        public double Value { get; } = value;
    }
}
