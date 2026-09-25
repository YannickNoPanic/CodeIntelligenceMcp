namespace CodeIntelligenceMcp.Roslyn.Models;

// Resolution outcome for a symbol query, so tools can tell "no results" apart from "wrong name".
public sealed record TargetLookup(bool Found, string? Error, string? Hint, IReadOnlyList<string> Candidates)
{
    public static readonly TargetLookup Ok = new(true, null, null, []);

    public static TargetLookup NotFound(string error, string hint, IReadOnlyList<string>? candidates = null) =>
        new(false, error, hint, candidates ?? []);
}
