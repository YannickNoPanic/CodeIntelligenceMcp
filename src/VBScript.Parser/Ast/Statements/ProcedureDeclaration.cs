using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public abstract class ProcedureDeclaration(MethodAccessModifier modifier, Identifier id, Statement body) : Statement
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public MethodAccessModifier AccessModifier { get; } = modifier;
        public List<Parameter> Parameters { get; } = [];
        public Statement Body { get; } = body ?? throw new ArgumentNullException(nameof(body));
    }

    public enum MethodAccessModifier
    {
        None,
        Public,
        Private,
        PublicDefault,
    }
}
