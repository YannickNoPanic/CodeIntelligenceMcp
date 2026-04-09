using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay("{Value}")]
    public class BooleanLiteral(bool value) : LiteralExpression
    {
        public bool Value { get; } = value;
    }
}
