using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast
{
    public abstract class VariableDeclarationNode(Identifier id, bool isDynamicArray) : Node
    {
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public bool IsDynamicArray { get; } = isDynamicArray;
        public List<int> ArrayDims { get; } = [];
    }
}
