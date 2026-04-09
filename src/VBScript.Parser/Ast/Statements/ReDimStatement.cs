namespace VBScript.Parser.Ast.Statements
{
    public class ReDimStatement(bool preserve) : Statement
    {
        public bool Preserve { get; } = preserve;
        public List<ReDimDeclaration> ReDims { get; } = [];
    }
}
