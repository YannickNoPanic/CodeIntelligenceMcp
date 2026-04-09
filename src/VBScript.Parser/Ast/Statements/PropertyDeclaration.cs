using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public abstract class PropertyDeclaration(MethodAccessModifier modifier, Identifier id) : Statement
    {
        public MethodAccessModifier AccessModifier { get; } = modifier;
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public List<Parameter> Parameters { get; } = [];
        public List<Statement> Body { get; } = [];
    }
}
