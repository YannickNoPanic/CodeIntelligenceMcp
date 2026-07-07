namespace CodeIntelligenceMcp.Common;

public static class SourceFileWalker
{
    // Prunes skip directories during recursion: the OS never descends into node_modules,
    // .venv, etc. Filtering Directory.EnumerateFiles(AllDirectories) afterwards would still
    // pay the full I/O cost of walking those trees.
    public static IEnumerable<string> EnumerateFiles(
        string rootPath,
        IEnumerable<string> extensions,
        IEnumerable<string> skipDirs,
        CancellationToken ct = default)
    {
        HashSet<string> extensionSet = new(extensions, StringComparer.OrdinalIgnoreCase);
        HashSet<string> skipSet = new(skipDirs, StringComparer.OrdinalIgnoreCase);

        Stack<string> pending = new();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = pending.Pop();

            string[] subDirs;
            string[] files;
            try
            {
                subDirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string subDir in subDirs)
            {
                if (!skipSet.Contains(Path.GetFileName(subDir)))
                    pending.Push(subDir);
            }

            foreach (string file in files)
            {
                if (extensionSet.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }
    }
}
