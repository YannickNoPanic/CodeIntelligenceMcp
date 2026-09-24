namespace Fixture.Core;

// Deliberate bait for the comment-too-long rule.
public class CommentBait
{
    /// <summary>
    /// A single-line summary is fine.
    /// </summary>
    public const string Url = "https://example.com//not-a-comment";

    // FLAG-LINE-BLOCK: this block
    // spans two lines.
    public int Blocked;

    /// <summary>
    /// FLAG-DOC-SUMMARY: this summary
    /// also spans two lines.
    /// </summary>
    public int Documented;

    public int Trailing; // a trailing comment is never a block
}
