using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class EraseStatement(Identifier id) : Statement
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
    }
}
