using System.Diagnostics;

namespace VBScript.Parser.Ast.Expressions
{
    [DebuggerDisplay(".{Property.Name}")]
    public class WithMemberAccessExpression(Identifier property) : Expression
    {
        public Identifier Property { get; } = property ?? throw new ArgumentNullException(nameof(property));
    }
}
