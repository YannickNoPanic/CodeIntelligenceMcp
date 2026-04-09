using System.Diagnostics;

namespace VBScript.Parser.Ast
{
    [DebuggerDisplay("{ToString()}")]
    public readonly struct Location(in Position start, in Position end) : IEquatable<Location>
    {
        public Position Start { get; } = start;
        public Position End { get; } = end;

        public Location WithPosition(in Position start, in Position end) =>
            new(start, end);

        public override bool Equals(object? obj) =>
            obj is Location other && Equals(other);

        public bool Equals(Location other) =>
            Start.Equals(other.Start)
            && End.Equals(other.End);

        public override int GetHashCode() =>
            unchecked((Start.GetHashCode() * 397) ^ End.GetHashCode());

        public override string ToString() => $"{Start}...{End}";

        public static bool operator ==(in Location left, in Location right) => left.Equals(right);
        public static bool operator !=(in Location left, in Location right) => !left.Equals(right);

        public void Deconstruct(out Position start, out Position end)
        {
            start = Start;
            end   = End;
        }
    }
}
