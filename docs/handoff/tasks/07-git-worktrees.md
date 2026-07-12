# Taak 7 — Git worktree-support + branch-fallbacks

**Status:** open. **Plansectie:** "Task 7". **Audit-bevinding:** R1 (blocker) —
in een git worktree is `.git` een FILE, `ResolveRepoRoot` eist een directory;
staleness en `analyze_changes` breken precies in het multi-agent-scenario
waarvoor de tool gepitcht wordt. Plus R11: unborn HEAD geeft NRE, base branch
`main` is hardcoded default zonder fallback.

## Bestanden

- `src/CodeIntelligenceMcp.Roslyn/Git/GitDiffService.cs`
- Nieuw: `tests/CodeIntelligenceMcp.Tests/GitDiffServiceTests.cs`

## Implementatie (TDD — tests eerst, zie plansectie voor volledige testcode)

1. **`ResolveRepoRoot` (regel ~54):** `if (Directory.Exists(gitPath) || File.Exists(gitPath)) return dir;`
   Overweeg `Repository.Discover` van LibGit2Sharp als alternatief, maar let op:
   die geeft het gitdir-pad, niet de working directory — de simpele
   File.Exists-toevoeging is veiliger.
2. **Unborn HEAD:** in `GetChangedFiles`, `GetChangedFilesIncludingWorkingTree`
   en `GetFileContentAtBase`: guard `repo.Head.Tip is null` → gooi
   `InvalidOperationException("Repository has no commits yet")` (tools vangen
   `InvalidOperationException` al af naar korte errors).
   Ook `ComputeFingerprint` gebruikt al `repo.Head.Tip?.Sha ?? "no-head"` — ok.
3. **`ResolveFromCommit`:** na `main`/`origin/main`-miss ook `master`/
   `origin/master` proberen, dan `repo.Head.TrackedBranch`; pas daarna de
   bestaande `ArgumentException`, uitgebreid met de eerste ~10 beschikbare
   branchnamen in de message.

## Tests (LibGit2Sharp, geen shell-out; temp-dirs met cleanup)

- Worktree-simulatie: init repo, maak submap met `.git`-FILE met inhoud
  `gitdir: <pad>`, assert `ResolveRepoRoot` de submap teruggeeft.
- Lege repo: `ComputeFingerprint` gooit niet.
- Repo met default branch `master` + 1 commit: `GetChangedFiles(dir, "main")` gooit niet.
- Cleanup-valkuil: libgit2-packfiles zijn read-only; vang
  `UnauthorizedAccessException` in Dispose of clear het read-only attribuut.

## Acceptatie

- Nieuwe tests groen, bestaande groen.
- Commit: `fix: git worktree root resolution, unborn HEAD guards, base-branch fallback`
