using System.Text.RegularExpressions;
using CodeIntelligenceMcp.AspClassic.Models;

namespace CodeIntelligenceMcp.AspClassic;

public sealed class AspIndex
{
    private static readonly Regex IncludeFilePattern = new(
        @"<!--\s*#include\s+file\s*=\s*""([^""]+)""\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IncludeVirtualPattern = new(
        @"<!--\s*#include\s+virtual\s*=\s*""([^""]+)""\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WordBoundaryPattern = new(
        @"\b{0}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, AspFileInfo> _files;
    private readonly IReadOnlyList<(string FilePath, SqlQueryInfo Query)> _allSql;
    private readonly string _rootPath;

    private AspIndex(
        IReadOnlyDictionary<string, AspFileInfo> files,
        IReadOnlyList<(string FilePath, SqlQueryInfo Query)> allSql,
        string rootPath)
    {
        _files = files;
        _allSql = allSql;
        _rootPath = rootPath;
    }

    public static AspIndex Build(string rootPath, Action<string>? log = null)
    {
        var files = new Dictionary<string, AspFileInfo>(StringComparer.OrdinalIgnoreCase);
        var allSql = new List<(string FilePath, SqlQueryInfo Query)>();
        int fallbackCount = 0;
        int expressionBlockCount = 0;
        int totalBlockCount = 0;

        foreach (string filePath in Directory.EnumerateFiles(rootPath, "*.asp", SearchOption.AllDirectories))
        {
            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (IOException)
            {
                log?.Invoke($"[warn] Could not read file: {filePath}");
                continue;
            }

            IReadOnlyList<IncludeRef> includes = ExtractIncludes(content, filePath, rootPath);
            IReadOnlyList<VbscriptBlock> blocks = AspBlockExtractor.Extract(content);

            var subs = new List<SubInfo>();
            var functions = new List<FunctionInfo>();
            var variables = new List<VariableInfo>();
            var sqlQueries = new List<SqlQueryInfo>();

            foreach (VbscriptBlock block in blocks)
            {
                totalBlockCount++;

                if (block.IsExpression)
                {
                    expressionBlockCount++;
                    continue;
                }

                Action<string>? blockLog = log is not null
                    ? msg => { fallbackCount++; log($"  {filePath}:{block.LineStart} — {msg}"); }
                    : null;

                ParsedBlock parsed = VbscriptParserAdapter.Parse(block.Source, block.LineStart, blockLog);
                subs.AddRange(parsed.Subs);
                functions.AddRange(parsed.Functions);
                variables.AddRange(parsed.Variables);

                IReadOnlyList<SqlQueryInfo> blockSql = SqlExtractor.Extract(block.Source, block.LineStart);
                sqlQueries.AddRange(blockSql);
            }

            AspFileInfo fileInfo = new(
                filePath,
                includes,
                subs,
                functions,
                variables,
                blocks);

            files[filePath] = fileInfo;

            foreach (SqlQueryInfo query in sqlQueries)
            {
                allSql.Add((filePath, query));
            }
        }

        log?.Invoke($"[info] ASP index complete: {files.Count} files, {totalBlockCount} blocks ({expressionBlockCount} expression blocks skipped, {fallbackCount} blocks used regex fallback)");

        return new AspIndex(files, allSql, rootPath);
    }

    public AspFileInfo? GetFile(string filePath)
    {
        if (_files.TryGetValue(filePath, out AspFileInfo? info))
            return info;

        string absolute = Path.GetFullPath(Path.Combine(_rootPath, filePath));
        _files.TryGetValue(absolute, out info);
        return info;
    }

    public IReadOnlyList<(string FilePath, int LineNumber, string Kind, string Context)> FindSymbol(string symbolName)
    {
        var results = new List<(string FilePath, int LineNumber, string Kind, string Context)>();
        Regex wordPattern = new($@"\b{Regex.Escape(symbolName)}\b", RegexOptions.IgnoreCase);

        foreach ((string filePath, AspFileInfo fileInfo) in _files)
        {
            foreach (SubInfo sub in fileInfo.Subs)
            {
                if (string.Equals(sub.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((filePath, sub.LineStart, "sub", sub.Name));
                }
            }

            foreach (FunctionInfo func in fileInfo.Functions)
            {
                if (string.Equals(func.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((filePath, func.LineStart, "function", func.Name));
                }
            }

            foreach (VariableInfo variable in fileInfo.Variables)
            {
                if (string.Equals(variable.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((filePath, variable.Line, "variable", variable.Name));
                }
            }

            foreach (VbscriptBlock block in fileInfo.VbscriptBlocks)
            {
                string[] lines = block.Source.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (wordPattern.IsMatch(line))
                    {
                        int fileLineNumber = block.LineStart + i;
                        results.Add((filePath, fileLineNumber, "call", line.Trim()));
                    }
                }
            }
        }

        return results;
    }

    public (IReadOnlyList<(string Path, string ResolvedPath, int Line, bool Exists)> Direct,
            IReadOnlyList<(string Path, string ResolvedPath, int Depth)> Transitive)
        GetIncludes(string filePath)
    {
        AspFileInfo? fileInfo = GetFile(filePath);
        if (fileInfo == null)
        {
            return (Array.Empty<(string, string, int, bool)>(), Array.Empty<(string, string, int)>());
        }

        string fileDirectory = Path.GetDirectoryName(fileInfo.FilePath) ?? _rootPath;

        var direct = fileInfo.Includes.Select(inc =>
        {
            string resolved = ResolveInclude(inc.Path, fileDirectory);
            bool exists = File.Exists(resolved);
            return (inc.Path, resolved, inc.Line, exists);
        }).ToList();

        var transitive = new List<(string Path, string ResolvedPath, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fileInfo.FilePath };
        var queue = new Queue<(string ResolvedPath, int Depth)>();

        foreach ((string path, string resolvedPath, int line, bool exists) in direct)
        {
            if (!visited.Contains(resolvedPath))
            {
                queue.Enqueue((resolvedPath, 1));
            }
        }

        while (queue.Count > 0)
        {
            (string currentPath, int depth) = queue.Dequeue();

            if (visited.Contains(currentPath))
                continue;

            visited.Add(currentPath);

            string relativePath = MakeRelative(currentPath, _rootPath);
            transitive.Add((relativePath, currentPath, depth));

            if (_files.TryGetValue(currentPath, out AspFileInfo? transitiveInfo))
            {
                string transitiveDir = Path.GetDirectoryName(currentPath) ?? _rootPath;
                foreach (IncludeRef inc in transitiveInfo.Includes)
                {
                    string resolved = ResolveIncludePath(inc.Path, transitiveDir);
                    if (!visited.Contains(resolved))
                    {
                        queue.Enqueue((resolved, depth + 1));
                    }
                }
            }
        }

        return (direct, transitive);
    }

    public IReadOnlyList<(string FilePath, int LineNumber, string Context)> Search(string query)
    {
        var results = new List<(string FilePath, int LineNumber, string Context)>();

        foreach ((string filePath, AspFileInfo fileInfo) in _files)
        {
            foreach (VbscriptBlock block in fileInfo.VbscriptBlocks)
            {
                string[] lines = block.Source.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        int fileLineNumber = block.LineStart + i;
                        results.Add((filePath, fileLineNumber, line.Trim()));
                    }
                }
            }
        }

        return results;
    }

    public IReadOnlyList<(string FilePath, SqlQueryInfo Query)> FindByTable(string tableName)
    {
        return [.. _allSql
            .Where(entry => entry.Query.Tables.Any(t =>
                string.Equals(t, tableName, StringComparison.OrdinalIgnoreCase)))];
    }

    public IReadOnlyList<SqlQueryInfo> GetFileQueries(string filePath)
    {
        return [.. _allSql
            .Where(entry => string.Equals(entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Query)];
    }

    public IReadOnlyList<(string TableName, int UsageCount, IReadOnlyList<(string FilePath, int UsageCount)> Files)> ListTables()
    {
        var tableGroups = _allSql
            .SelectMany(entry => entry.Query.Tables.Select(t => (TableName: t, entry.FilePath)))
            .GroupBy(x => x.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                int usageCount = g.Count();
                IReadOnlyList<(string FilePath, int UsageCount)> files = g
                    .GroupBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(fg => (fg.Key, fg.Count()))
                    .ToList();
                return (TableName: g.Key, UsageCount: usageCount, Files: files);
            })
            .OrderByDescending(x => x.UsageCount)
            .ToList();

        return tableGroups;
    }

    public IReadOnlyList<(string FilePath, int LineNumber, string? TableName, string Operation, string Signature)> FindByColumn(string columnName)
    {
        return _allSql
            .Where(entry => entry.Query.Columns.Any(c =>
                string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase)))
            .Select(entry =>
            {
                string? tableName = entry.Query.Tables.Count > 0 ? entry.Query.Tables[0] : null;
                return (entry.FilePath, entry.Query.LineStart, tableName, entry.Query.Operation, entry.Query.Signature);
            })
            .ToList();
    }

    private static IReadOnlyList<IncludeRef> ExtractIncludes(string content, string filePath, string rootPath)
    {
        var includes = new List<IncludeRef>();
        string fileDirectory = Path.GetDirectoryName(filePath) ?? rootPath;
        string[] lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int lineNumber = i + 1;

            foreach (Match match in IncludeFilePattern.Matches(line))
            {
                includes.Add(new IncludeRef(match.Groups[1].Value, lineNumber));
            }

            foreach (Match match in IncludeVirtualPattern.Matches(line))
            {
                includes.Add(new IncludeRef(match.Groups[1].Value, lineNumber));
            }
        }

        return includes;
    }

    private string ResolveInclude(string includePath, string fileDirectory)
    {
        if (includePath.StartsWith('/') || includePath.StartsWith('\\'))
        {
            return Path.GetFullPath(Path.Combine(_rootPath, includePath.TrimStart('/', '\\')));
        }

        return Path.GetFullPath(Path.Combine(fileDirectory, includePath));
    }

    private string ResolveIncludePath(string includePath, string fileDirectory)
    {
        return ResolveInclude(includePath, fileDirectory);
    }

    private static string MakeRelative(string absolutePath, string rootPath)
    {
        if (absolutePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            string relative = absolutePath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            return relative;
        }

        return absolutePath;
    }
}
