using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay("{Value}")]
    public class IntegerLiteral(int value) : LiteralExpression
    {
        public int Value { get; } = value;
    }
}
