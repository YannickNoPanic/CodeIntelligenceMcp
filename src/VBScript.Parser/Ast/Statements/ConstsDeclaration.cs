namespace VBScript.Parser.Ast.Statements
{
    public class ConstsDeclaration(MemberAccessModifier modifier) : Statement
    {
        public MemberAccessModifier Modifier { get; } = modifier;
        public List<ConstDeclaration> Declarations { get; } = [];
    }

    public enum MemberAccessModifier
    {
        None,
        Public,
        Private,
    }
}
