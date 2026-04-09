namespace VBScript.Parser
{
    internal struct Marker(int index, int line, int column)
    {
        public int Index = index;
        public int Line = line;
        public int Column = column;
    }
}
