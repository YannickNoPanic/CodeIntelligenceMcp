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
            // In a git worktree .git is a file ("gitdir: <path>"), not a directory —
            // the parallel-worktree scenario this server is built for.
            string gitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static IReadOnlyList<ChangedFile> GetChangedFiles(string repoPath, string baseBranch)
    {
        using Repository repo = OpenRepository(repoPath);

        Commit headCommit = repo.Head.Tip
            ?? throw new InvalidOperationException("Repository has no commits yet");
        Commit fromCommit = ResolveFromCommit(repo, baseBranch);

        TreeChanges changes = repo.Diff.Compare<TreeChanges>(fromCommit.Tree, headCommit.Tree);

        return [.. changes.Select(c => new ChangedFile(
            c.Path,
            MapStatus(c.Status),
            c.OldPath != c.Path ? c.OldPath : null))];
    }

    public static IReadOnlyList<ChangedFile> GetChangedFilesIncludingWorkingTree(string repoPath, string baseBranch)
    {
        using Repository repo = OpenRepository(repoPath);

        if (repo.Head.Tip is null)
            throw new InvalidOperationException("Repository has no commits yet");

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
        using Repository repo = OpenRepository(repoPath);

        if (repo.Head.Tip is null)
            throw new InvalidOperationException("Repository has no commits yet");

        Commit fromCommit = ResolveFromCommit(repo, baseBranch);

        string gitPath = filePath.Replace('\\', '/');
        TreeEntry? entry = fromCommit[gitPath];
        if (entry?.Target is Blob blob)
            return blob.GetContentText();

        return null;
    }

    private static Commit ResolveFromCommit(Repository repo, string baseBranch)
    {
        // Only branch-shaped names fall back to master; an explicit commit (sha, HEAD~3) must resolve or fail.
        Commit? baseTip = FindBranch(repo, baseBranch)?.Tip
            ?? FindBranch(repo, $"origin/{baseBranch}")?.Tip
            ?? FindCommit(repo, baseBranch);

        if (baseTip is null && Reference.IsValidName($"refs/heads/{baseBranch}"))
        {
            baseTip = FindBranch(repo, "master")?.Tip
                ?? FindBranch(repo, "origin/master")?.Tip
                ?? repo.Head.TrackedBranch?.Tip;
        }

        if (baseTip is null)
        {
            string available = string.Join(", ", repo.Branches
                .Where(b => !b.IsRemote)
                .Select(b => b.FriendlyName)
                .Take(10));
            throw new ArgumentException(
                $"Branch or commit '{baseBranch}' not found in repository — available branches: {available}");
        }

        Commit? mergeBase = repo.ObjectDatabase.FindMergeBase(baseTip, repo.Head.Tip);
        return mergeBase ?? baseTip;
    }

    private static Repository OpenRepository(string repoPath)
    {
        try
        {
            return new Repository(repoPath);
        }
        catch (LibGit2SharpException ex)
        {
            string hint = ex.Message.Contains("not owned", StringComparison.OrdinalIgnoreCase)
                ? $" Mark it safe with: git config --global --add safe.directory \"{repoPath.Replace('\\', '/')}\""
                : string.Empty;
            throw new InvalidOperationException($"Cannot open git repository at '{repoPath}': {ex.Message}.{hint}", ex);
        }
    }

    private static Branch? FindBranch(Repository repo, string name)
    {
        try
        {
            return repo.Branches[name];
        }
        catch (InvalidSpecificationException)
        {
            return null;
        }
    }

    private static Commit? FindCommit(Repository repo, string revision)
    {
        try
        {
            return repo.Lookup<Commit>(revision);
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }

    private static string MapStatus(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Deleted => "deleted",
        ChangeKind.Renamed => "renamed",
        _ => "modified"
    };
}
