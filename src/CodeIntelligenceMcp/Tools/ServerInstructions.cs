namespace CodeIntelligenceMcp.Tools;

// Sent to clients at initialize; Claude Code and Codex place it in the system prompt every session.
internal static class ServerInstructions
{
    public const string Text = """
        Semantic code index (Roslyn for .NET; parsers for ASP Classic, PowerShell, Python, JS/TS/Vue).
        Prefer these tools over grep/file reads when the question is about code structure, not text:
        - Where is X used / who calls X / what implements or derives from X: find_usages, find_callers (depth 2-3 for impact), find_implementations, find_derived_types. Grep misses overloads, generics and interface dispatch; these do not.
        - Before a refactor or rename: get_change_risk on the type, then find_callers.
        - Reviewing a branch or checking work before commit: analyze_changes (baseBranch may be a branch or commit; includeUncommitted=true before committing).
        - Compile errors without a build: get_diagnostics.
        - First look at an unfamiliar .NET solution: get_codebase_wiki, then get_hotspots.
        Workspaces: call list_workspaces to see configured names. Any absolute .sln/.slnx/.slnf path works as the workspace argument, including git worktrees.
        The first call on a large solution indexes it and can take a minute; later calls are fast.
        If the tools are deferred in your client, load the ones you need in one batch.
        If a workspace is not indexed or a tool is insufficient, say so and fall back to file reads.
        """;
}
