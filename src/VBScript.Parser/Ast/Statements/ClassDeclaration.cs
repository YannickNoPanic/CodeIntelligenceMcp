using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class ClassDeclaration(Identifier id) : Statement
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public List<Statement> Members { get; } = [];
    }
}
