namespace VBScript.Parser.Ast.Statements
{
    public class VariablesDeclaration : Statement
    {
        public List<VariableDeclaration> Variables { get; } = [];
    }
}
