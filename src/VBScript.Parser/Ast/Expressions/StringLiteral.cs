using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay("{Value}")]
    public class StringLiteral(string value) : LiteralExpression
    {
        public string Value { get; } = value;
    }
}
