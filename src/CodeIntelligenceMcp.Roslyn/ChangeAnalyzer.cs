using CodeIntelligenceMcp.Roslyn.Git;
using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class ChangeAnalyzer(RoslynWorkspaceIndex index, CleanArchitectureNames cleanArch)
{
    private static readonly string[] AllViolationRules =
    [
        "core-no-ef", "core-no-http", "core-no-azure",
        "usecase-not-sealed",
        "inline-viewmodel-razor", "business-logic-in-razor", "json-parsing-in-view",
        "controller-not-thin", "dto-in-core"
    ];

    public async Task<ChangeAnalysis> AnalyzeAsync(
        string workspace,
        string solutionPath,
        string baseBranch,
        bool includeSignatures,
        bool includeDiagnostics,
        CancellationToken ct)
    {
        string? repoRoot = GitDiffService.ResolveRepoRoot(solutionPath);
        if (repoRoot is null)
            throw new InvalidOperationException("Could not find git repository root from solution path");

        IReadOnlyList<ChangedFile> allChanges = GitDiffService.GetChangedFiles(repoRoot, baseBranch);

        string solutionDir = (Path.GetDirectoryName(solutionPath) ?? string.Empty).Replace('\\', '/');

        List<ChangedFile> workspaceFiles = [.. allChanges.Where(f =>
        {
            string fullPath = Path.GetFullPath(Path.Combine(repoRoot, f.FilePath)).Replace('\\', '/');
            return fullPath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase);
        })];

        HashSet<string> changedFilePaths = [.. workspaceFiles.Select(f =>
            Path.GetFullPath(Path.Combine(repoRoot, f.FilePath)).Replace('\\', '/'))];

        List<ChangedFileDetail> fileDetails = BuildFileDetails(workspaceFiles, repoRoot);

        HashSet<string> domains = BuildAffectedDomains(workspaceFiles, repoRoot);

        List<SignatureChange> signatureChanges = [];
        if (includeSignatures)
            signatureChanges = await DetectSignatureChangesAsync(workspaceFiles, repoRoot, baseBranch, ct);

        ViolationDetector detector = new(index, cleanArch);
        List<ViolationResult> scopedViolations = [];
        foreach (string rule in AllViolationRules)
        {
            foreach (ViolationResult v in detector.Detect(rule))
            {
                if (changedFilePaths.Contains(v.FilePath.Replace('\\', '/')))
                    scopedViolations.Add(v);
            }
        }

        List<DiagnosticResult> scopedDiagnostics = [];
        if (includeDiagnostics)
        {
            IReadOnlyList<DiagnosticResult> all = await index.GetCompilerDiagnosticsAsync(ct: ct);
            scopedDiagnostics = [.. all.Where(d =>
                changedFilePaths.Contains(d.FilePath.Replace('\\', '/')))];
        }

        List<TypeSummary> newTypes = [];
        foreach (ChangedFile file in workspaceFiles.Where(f => f.Status == "added"))
        {
            string fullPath = Path.GetFullPath(Path.Combine(repoRoot, file.FilePath));
            newTypes.AddRange(index.GetTypesInFile(fullPath));
        }

        ChangeSummary summary = new(
            workspaceFiles.Count,
            workspaceFiles.Count(f => f.Status == "added"),
            workspaceFiles.Count(f => f.Status == "deleted"),
            [.. domains.OrderBy(d => d)],
            scopedViolations.Count,
            scopedDiagnostics.Count,
            signatureChanges.Count);

        return new ChangeAnalysis(
            workspace,
            baseBranch,
            summary,
            fileDetails,
            signatureChanges,
            scopedViolations,
            scopedDiagnostics,
            newTypes);
    }

    private List<ChangedFileDetail> BuildFileDetails(IReadOnlyList<ChangedFile> files, string repoRoot)
    {
        return [.. files.Select(f =>
        {
            string fullPath = Path.GetFullPath(Path.Combine(repoRoot, f.FilePath));
            IReadOnlyList<TypeSummary> types = index.GetTypesInFile(fullPath);
            return new ChangedFileDetail(f.FilePath, f.Status, [.. types.Select(t => t.Name)]);
        })];
    }

    private HashSet<string> BuildAffectedDomains(IReadOnlyList<ChangedFile> files, string repoRoot)
    {
        HashSet<string> domains = [];
        foreach (ChangedFile file in files)
        {
            string fullPath = Path.GetFullPath(Path.Combine(repoRoot, file.FilePath));
            foreach (TypeSummary type in index.GetTypesInFile(fullPath))
            {
                string domain = GetDomainFromNamespace(type.Namespace);
                if (!string.IsNullOrEmpty(domain))
                    domains.Add(domain);
            }
        }
        return domains;
    }

    private static async Task<List<SignatureChange>> DetectSignatureChangesAsync(
        IReadOnlyList<ChangedFile> files,
        string repoRoot,
        string baseBranch,
        CancellationToken ct)
    {
        List<SignatureChange> changes = [];
        foreach (ChangedFile file in files.Where(f =>
            f.Status == "modified"
            && f.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            string? baseContent = GitDiffService.GetFileContentAtBase(repoRoot, baseBranch, file.FilePath);
            if (baseContent is null)
                continue;

            string fullPath = Path.GetFullPath(Path.Combine(repoRoot, file.FilePath));
            if (!File.Exists(fullPath))
                continue;

            string currentContent = await File.ReadAllTextAsync(fullPath, ct);
            changes.AddRange(CompareSignatures(baseContent, currentContent));
        }
        return changes;
    }

    private static IEnumerable<SignatureChange> CompareSignatures(string baseSource, string currentSource)
    {
        Dictionary<string, string> baseSignatures = ExtractMemberSignatures(CSharpSyntaxTree.ParseText(baseSource).GetRoot());
        Dictionary<string, string> currentSignatures = ExtractMemberSignatures(CSharpSyntaxTree.ParseText(currentSource).GetRoot());

        foreach ((string key, string sig) in currentSignatures)
        {
            if (!baseSignatures.TryGetValue(key, out string? baseSig))
            {
                (string typeName, string memberName) = SplitKey(key);
                yield return new SignatureChange(typeName, memberName, null, sig, "added");
            }
            else if (baseSig != sig)
            {
                (string typeName, string memberName) = SplitKey(key);
                yield return new SignatureChange(typeName, memberName, baseSig, sig, "modified");
            }
        }

        foreach ((string key, string sig) in baseSignatures)
        {
            if (!currentSignatures.ContainsKey(key))
            {
                (string typeName, string memberName) = SplitKey(key);
                yield return new SignatureChange(typeName, memberName, sig, null, "removed");
            }
        }
    }

    private static Dictionary<string, string> ExtractMemberSignatures(Microsoft.CodeAnalysis.SyntaxNode root)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);

        foreach (TypeDeclarationSyntax typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            string typeName = typeDecl.Identifier.Text;

            foreach (MethodDeclarationSyntax method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(m => m.Text == "public"))
                    continue;

                string key = $"{typeName}::{method.Identifier.Text}({method.ParameterList})";
                result[key] = $"{method.ReturnType} {method.Identifier.Text}({method.ParameterList})";
            }

            foreach (PropertyDeclarationSyntax prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (!prop.Modifiers.Any(m => m.Text == "public"))
                    continue;

                string key = $"{typeName}::{prop.Identifier.Text}";
                result[key] = $"{prop.Type} {prop.Identifier.Text}";
            }
        }

        return result;
    }

    private static (string TypeName, string MemberName) SplitKey(string key)
    {
        int sep = key.IndexOf("::", StringComparison.Ordinal);
        return sep < 0 ? (string.Empty, key) : (key[..sep], key[(sep + 2)..]);
    }

    private static string GetDomainFromNamespace(string @namespace)
    {
        string[] parts = @namespace.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Application", StringComparison.OrdinalIgnoreCase)
                || parts[i].Equals("Features", StringComparison.OrdinalIgnoreCase)
                || parts[i].Equals("UseCases", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return string.Empty;
    }
}
