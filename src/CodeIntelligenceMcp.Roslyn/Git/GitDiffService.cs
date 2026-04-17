using LibGit2Sharp;

namespace CodeIntelligenceMcp.Roslyn.Git;

public static class GitDiffService
{
    public static string? ResolveRepoRoot(string startPath)
    {
        string? dir = File.Exists(startPath) ? Path.GetDirectoryName(startPath) : startPath;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static IReadOnlyList<ChangedFile> GetChangedFiles(string repoPath, string baseBranch)
    {
        using Repository repo = new(repoPath);

        Commit fromCommit = ResolveFromCommit(repo, baseBranch);
        Commit headCommit = repo.Head.Tip;

        TreeChanges changes = repo.Diff.Compare<TreeChanges>(fromCommit.Tree, headCommit.Tree);

        return [.. changes.Select(c => new ChangedFile(
            c.Path,
            MapStatus(c.Status),
            c.OldPath != c.Path ? c.OldPath : null))];
    }

    public static string? GetFileContentAtBase(string repoPath, string baseBranch, string filePath)
    {
        using Repository repo = new(repoPath);

        Commit fromCommit = ResolveFromCommit(repo, baseBranch);

        string gitPath = filePath.Replace('\\', '/');
        TreeEntry? entry = fromCommit[gitPath];
        if (entry?.Target is Blob blob)
            return blob.GetContentText();

        return null;
    }

    private static Commit ResolveFromCommit(Repository repo, string baseBranch)
    {
        Branch? branch = repo.Branches[baseBranch]
            ?? repo.Branches[$"origin/{baseBranch}"];

        if (branch is null)
            throw new ArgumentException($"Branch '{baseBranch}' not found in repository");

        Commit? mergeBase = repo.ObjectDatabase.FindMergeBase(branch.Tip, repo.Head.Tip);
        return mergeBase ?? branch.Tip;
    }

    private static string MapStatus(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Deleted => "deleted",
        ChangeKind.Renamed => "renamed",
        _ => "modified"
    };
}
