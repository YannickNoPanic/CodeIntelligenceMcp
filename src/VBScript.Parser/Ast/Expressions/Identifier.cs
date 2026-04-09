using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay("{Name}")]
    public class Identifier(string name) : Expression
    {
        public static readonly int MaxLength = 255;

        public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    }
}
