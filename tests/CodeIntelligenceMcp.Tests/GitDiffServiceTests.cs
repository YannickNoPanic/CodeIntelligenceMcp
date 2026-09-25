using CodeIntelligenceMcp.Roslyn.Git;
using FluentAssertions;
using LibGit2Sharp;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class GitDiffServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-git").FullName;

    public void Dispose()
    {
        // libgit2 pack/object files are read-only; clear attributes before deleting.
        try
        {
            foreach (string file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Signature TestSignature => new("test", "test@test.local", DateTimeOffset.Now);

    private void CommitFile(Repository repo, string name)
    {
        File.WriteAllText(Path.Combine(_dir, name), "content");
        Commands.Stage(repo, name);
        repo.Commit("init", TestSignature, TestSignature);
    }

    [Fact]
    public void ResolveRepoRoot_GitFileInsteadOfDirectory_ResolvesRoot()
    {
        Repository.Init(_dir);
        string worktreeDir = Path.Combine(_dir, "wt");
        Directory.CreateDirectory(worktreeDir);
        File.WriteAllText(Path.Combine(worktreeDir, ".git"), $"gitdir: {Path.Combine(_dir, ".git")}");

        string? root = GitDiffService.ResolveRepoRoot(Path.Combine(worktreeDir, "some.sln"));

        root.Should().Be(worktreeDir);
    }

    [Fact]
    public void ResolveRepoRoot_RegularRepo_ResolvesRoot()
    {
        Repository.Init(_dir);

        string? root = GitDiffService.ResolveRepoRoot(Path.Combine(_dir, "some.sln"));

        root.Should().Be(_dir);
    }

    [Fact]
    public void ComputeFingerprint_EmptyRepoUnbornHead_ReturnsValueNotThrow()
    {
        Repository.Init(_dir);

        Action act = () => GitDiffService.ComputeFingerprint(_dir);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetChangedFiles_UnbornHead_ThrowsInvalidOperationNotNre()
    {
        Repository.Init(_dir);

        Action act = () => GitDiffService.GetChangedFiles(_dir, "main");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no commits*");
    }

    [Fact]
    public void GetChangedFiles_MainMissingMasterPresent_FallsBackToMaster()
    {
        Repository.Init(_dir);
        using (Repository repo = new(_dir))
        {
            // Default branch name may be main or master depending on git config;
            // force a branch named master to exist at HEAD either way.
            CommitFile(repo, "a.txt");
            if (repo.Head.FriendlyName != "master")
                repo.Branches.Add("master", repo.Head.Tip);
        }

        Action act = () => GitDiffService.GetChangedFiles(_dir, "main");

        act.Should().NotThrow();
    }

    [Fact]
    public void GetChangedFiles_NoMatchingBranch_ErrorListsAvailableBranches()
    {
        Repository.Init(_dir);
        using (Repository repo = new(_dir))
        {
            CommitFile(repo, "a.txt");
            if (repo.Head.FriendlyName != "trunk")
            {
                Branch trunk = repo.Branches.Add("trunk", repo.Head.Tip);
                Commands.Checkout(repo, trunk);
                foreach (Branch b in repo.Branches.Where(b => !b.IsRemote && b.FriendlyName != "trunk").ToList())
                    repo.Branches.Remove(b);
            }
        }

        Action act = () => GitDiffService.GetChangedFiles(_dir, "main");

        act.Should().Throw<ArgumentException>().WithMessage("*trunk*");
    }

    [Theory]
    [InlineData("HEAD~1")]
    [InlineData("HEAD^")]
    public void GetChangedFiles_RelativeCommitRef_DiffsAgainstThatCommit(string baseRef)
    {
        Repository.Init(_dir);
        using (Repository repo = new(_dir))
        {
            CommitFile(repo, "a.txt");
            CommitFile(repo, "b.txt");
        }

        IReadOnlyList<ChangedFile> changed = GitDiffService.GetChangedFiles(_dir, baseRef);

        changed.Should().ContainSingle(f => f.FilePath == "b.txt");
    }

    [Fact]
    public void GetChangedFiles_CommitSha_DiffsAgainstThatCommit()
    {
        Repository.Init(_dir);
        string firstSha;
        using (Repository repo = new(_dir))
        {
            CommitFile(repo, "a.txt");
            firstSha = repo.Head.Tip.Sha[..7];
            CommitFile(repo, "b.txt");
        }

        IReadOnlyList<ChangedFile> changed = GitDiffService.GetChangedFiles(_dir, firstSha);

        changed.Should().ContainSingle(f => f.FilePath == "b.txt");
    }

    [Fact]
    public void GetChangedFiles_NotARepository_ThrowsInvalidOperationNotLibGit2Exception()
    {
        Action act = () => GitDiffService.GetChangedFiles(_dir, "main");

        act.Should().Throw<InvalidOperationException>().WithMessage("Cannot open git repository*");
    }

    [Fact]
    public void GetChangedFiles_UnresolvableRef_ThrowsArgumentException()
    {
        Repository.Init(_dir);
        using (Repository repo = new(_dir))
            CommitFile(repo, "a.txt");

        Action act = () => GitDiffService.GetChangedFiles(_dir, "HEAD~99");

        act.Should().Throw<ArgumentException>().WithMessage("*HEAD~99*");
    }
}
