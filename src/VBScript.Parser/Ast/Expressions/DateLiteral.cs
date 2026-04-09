using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay("{Value}")]
    public class DateLiteral(DateTime value) : LiteralExpression
    {
        public DateTime Value { get; } = value;
    }
}
