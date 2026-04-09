using System.Globalization;

namespace VBScript.Parser.Ast
{
    public readonly struct Position(int line, int column) : IEquatable<Position>
    {
        public int Line { get; } = line;
        public int Column { get; } = column;

        public override bool Equals(object? obj) =>
            obj is Position other && Equals(other);

        public bool Equals(Position other) =>
            Line == other.Line && Column == other.Column;

        public override int GetHashCode() =>
            unchecked((Line * 397) ^ Column);

        public override string ToString()
            => Line.ToString(CultureInfo.InvariantCulture)
             + ","
             + Column.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(Position left, Position right) => left.Equals(right);
        public static bool operator !=(Position left, Position right) => !left.Equals(right);

        public void Deconstruct(out int line, out int column)
        {
            line = Line;
            column = Column;
        }
    }
}
