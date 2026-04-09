namespace VBScript.Parser.Ast.Statements
{
    public class FieldsDeclaration(FieldAccessModifier modifier) : Statement
    {
        public FieldAccessModifier Modifier { get; } = modifier;
        public List<FieldDeclaration> Fields { get; } = [];
    }

    public enum FieldAccessModifier
    {
        Private,
        Public,
    }
}
