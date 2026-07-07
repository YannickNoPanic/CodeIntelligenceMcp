using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;

namespace CodeIntelligenceMcp.Roslyn.Git;

public static class GitDiffService
{
    // Cheap staleness signal: HEAD sha plus the dirty file list with mtimes. If this differs
    // from the value captured at index build, the in-memory index no longer matches the disk.
    public static string? ComputeFingerprint(string startPath)
    {
        string? repoRoot = ResolveRepoRoot(startPath);
        if (repoRoot is null)
            return null;

        try
        {
            using Repository repo = new(repoRoot);

            StringBuilder sb = new(repo.Head.Tip?.Sha ?? "no-head");

            RepositoryStatus status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true
            });

            foreach (StatusEntry entry in status
                .Where(e => e.State != FileStatus.Ignored)
                .OrderBy(e => e.FilePath, StringComparer.Ordinal))
            {
                sb.Append('|').Append(entry.FilePath);

                string fullPath = Path.Combine(repoRoot, entry.FilePath);
                if (File.Exists(fullPath))
                    sb.Append(':').Append(File.GetLastWriteTimeUtc(fullPath).Ticks);
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash)[..16];
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }

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

    public static IReadOnlyList<ChangedFile> GetChangedFilesIncludingWorkingTree(string repoPath, string baseBranch)
    {
        using Repository repo = new(repoPath);

        Commit fromCommit = ResolveFromCommit(repo, baseBranch);

        TreeChanges committed = repo.Diff.Compare<TreeChanges>(fromCommit.Tree, repo.Head.Tip.Tree);
        TreeChanges uncommitted = repo.Diff.Compare<TreeChanges>(
            repo.Head.Tip.Tree,
            DiffTargets.WorkingDirectory | DiffTargets.Index);

        Dictionary<string, ChangedFile> merged = new(StringComparer.Ordinal);
        foreach (TreeEntryChanges c in committed)
            merged[c.Path] = new ChangedFile(c.Path, MapStatus(c.Status), c.OldPath != c.Path ? c.OldPath : null);
        foreach (TreeEntryChanges c in uncommitted)
            merged[c.Path] = new ChangedFile(c.Path, MapStatus(c.Status), c.OldPath != c.Path ? c.OldPath : null);

        return [.. merged.Values];
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
