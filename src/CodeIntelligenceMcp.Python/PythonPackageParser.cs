using System.Text.RegularExpressions;
using CodeIntelligenceMcp.Python.Models;
using Tomlyn;
using Tomlyn.Model;

namespace CodeIntelligenceMcp.Python;

public static class PythonPackageParser
{
    private static readonly Regex PackageSpec =
        new(@"^([A-Za-z0-9_\-\.]+)\s*([><=!~^,\s\d.]*)\s*$", RegexOptions.Compiled);

    public static PythonProjectInfo ParseProjectInfo(string rootPath)
    {
        string? projectName = null;
        string? pythonVersion = null;
        var dependencies = new List<PythonPackageInfo>();
        var devDependencies = new List<PythonPackageInfo>();

        // Try pyproject.toml first
        string pyprojectPath = Path.Combine(rootPath, "pyproject.toml");
        if (File.Exists(pyprojectPath))
        {
            try
            {
                ParsePyprojectToml(pyprojectPath, ref projectName, ref pythonVersion, dependencies, devDependencies);
            }
            catch (Exception)
            {
                // Fall through to other sources
            }
        }

        // requirements.txt (merge into deps if not already populated from pyproject)
        string requirementsPath = Path.Combine(rootPath, "requirements.txt");
        if (File.Exists(requirementsPath) && dependencies.Count == 0)
        {
            try
            {
                ParseRequirementsTxt(requirementsPath, "requirements.txt", dependencies);
            }
            catch (Exception)
            {
                // Ignore parse errors
            }
        }

        // requirements-dev.txt or requirements/dev.txt
        foreach (string devReqPath in new[]
        {
            Path.Combine(rootPath, "requirements-dev.txt"),
            Path.Combine(rootPath, "requirements", "dev.txt"),
            Path.Combine(rootPath, "requirements", "test.txt")
        })
        {
            if (File.Exists(devReqPath))
            {
                try { ParseRequirementsTxt(devReqPath, Path.GetFileName(devReqPath), devDependencies); }
                catch (Exception) { }
            }
        }

        // setup.cfg fallback
        string setupCfgPath = Path.Combine(rootPath, "setup.cfg");
        if (File.Exists(setupCfgPath) && dependencies.Count == 0)
        {
            try { ParseSetupCfg(setupCfgPath, ref projectName, dependencies, devDependencies); }
            catch (Exception) { }
        }

        return new PythonProjectInfo(projectName, pythonVersion, dependencies, devDependencies);
    }

    private static void ParsePyprojectToml(
        string path,
        ref string? projectName,
        ref string? pythonVersion,
        List<PythonPackageInfo> deps,
        List<PythonPackageInfo> devDeps)
    {
        string content = File.ReadAllText(path);
        TomlTable model = Toml.ToModel(content);

        // PEP 621 / Flit format: [project]
        if (model.TryGetValue("project", out object? projectObj) && projectObj is TomlTable project)
        {
            if (project.TryGetValue("name", out object? nameObj))
                projectName = nameObj?.ToString();

            if (project.TryGetValue("requires-python", out object? pyVerObj))
                pythonVersion = pyVerObj?.ToString();

            // [project].dependencies
            if (project.TryGetValue("dependencies", out object? depsObj) && depsObj is TomlArray depsArr)
            {
                foreach (object? dep in depsArr)
                {
                    if (dep is string depStr)
                    {
                        PythonPackageInfo? pkg = ParsePackageSpec(depStr, "pyproject.toml");
                        if (pkg is not null) deps.Add(pkg);
                    }
                }
            }

            // [project.optional-dependencies] — treat all as dev deps for simplicity
            if (project.TryGetValue("optional-dependencies", out object? optDepsObj) && optDepsObj is TomlTable optDeps)
            {
                foreach ((string _, object? groupObj) in optDeps)
                {
                    if (groupObj is TomlArray groupArr)
                    {
                        foreach (object? dep in groupArr)
                        {
                            if (dep is string depStr)
                            {
                                PythonPackageInfo? pkg = ParsePackageSpec(depStr, "pyproject.toml");
                                if (pkg is not null) devDeps.Add(pkg);
                            }
                        }
                    }
                }
            }
        }

        // Poetry format: [tool.poetry]
        if (model.TryGetValue("tool", out object? toolObj) && toolObj is TomlTable tool &&
            tool.TryGetValue("poetry", out object? poetryObj) && poetryObj is TomlTable poetry)
        {
            projectName ??= poetry.TryGetValue("name", out object? nameObj) ? nameObj?.ToString() : null;

            if (poetry.TryGetValue("dependencies", out object? depsObj) && depsObj is TomlTable poetryDeps)
            {
                foreach ((string pkgName, object? versionObj) in poetryDeps)
                {
                    if (pkgName.Equals("python", StringComparison.OrdinalIgnoreCase))
                    {
                        pythonVersion ??= versionObj?.ToString();
                        continue;
                    }

                    string? version = versionObj switch
                    {
                        string s => s,
                        TomlTable t => t.TryGetValue("version", out object? v) ? v?.ToString() : null,
                        _ => null
                    };

                    deps.Add(new PythonPackageInfo(pkgName, version, "pyproject.toml"));
                }
            }

            if (poetry.TryGetValue("dev-dependencies", out object? devDepsObj) && devDepsObj is TomlTable poetryDevDeps)
            {
                foreach ((string pkgName, object? versionObj) in poetryDevDeps)
                {
                    string? version = versionObj?.ToString();
                    devDeps.Add(new PythonPackageInfo(pkgName, version, "pyproject.toml"));
                }
            }

            // [tool.poetry.group.*.dependencies] (Poetry 1.2+ format)
            if (poetry.TryGetValue("group", out object? groupsObj) && groupsObj is TomlTable groups)
            {
                foreach ((string _, object? groupObj) in groups)
                {
                    if (groupObj is TomlTable group &&
                        group.TryGetValue("dependencies", out object? groupDepsObj) &&
                        groupDepsObj is TomlTable groupDeps)
                    {
                        foreach ((string pkgName, object? versionObj) in groupDeps)
                        {
                            string? version = versionObj?.ToString();
                            devDeps.Add(new PythonPackageInfo(pkgName, version, "pyproject.toml"));
                        }
                    }
                }
            }
        }
    }

    private static void ParseRequirementsTxt(string path, string source, List<PythonPackageInfo> result)
    {
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();

            // Skip comments, blank lines, options, URLs, -r includes
            if (string.IsNullOrEmpty(line) ||
                line.StartsWith('#') ||
                line.StartsWith('-') ||
                line.StartsWith("http", StringComparison.Ordinal))
                continue;

            // Remove inline comment
            int hashIdx = line.IndexOf(" #", StringComparison.Ordinal);
            if (hashIdx >= 0)
                line = line[..hashIdx].Trim();

            // Remove extras: "package[extras]>=1.0" → "package>=1.0"
            line = Regex.Replace(line, @"\[.*?\]", string.Empty);

            PythonPackageInfo? pkg = ParsePackageSpec(line, source);
            if (pkg is not null)
                result.Add(pkg);
        }
    }

    private static void ParseSetupCfg(
        string path,
        ref string? projectName,
        List<PythonPackageInfo> deps,
        List<PythonPackageInfo> devDeps)
    {
        bool inInstallRequires = false;
        bool inExtrasRequire = false;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith('['))
            {
                inInstallRequires = false;
                inExtrasRequire = false;
                continue;
            }

            if (trimmed.StartsWith("name", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
            {
                projectName ??= trimmed.Split('=', 2)[1].Trim();
                continue;
            }

            if (trimmed.StartsWith("install_requires", StringComparison.OrdinalIgnoreCase))
            {
                inInstallRequires = true;
                string afterEq = trimmed.Contains('=') ? trimmed.Split('=', 2)[1].Trim() : string.Empty;
                if (!string.IsNullOrEmpty(afterEq))
                {
                    PythonPackageInfo? pkg = ParsePackageSpec(afterEq, "setup.cfg");
                    if (pkg is not null) deps.Add(pkg);
                }
                continue;
            }

            if (trimmed.StartsWith("extras_require", StringComparison.OrdinalIgnoreCase))
            {
                inInstallRequires = false;
                inExtrasRequire = true;
                continue;
            }

            // Continuation line (indented)
            if ((inInstallRequires || inExtrasRequire) && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                string spec = trimmed.TrimStart('-').Trim();
                if (!string.IsNullOrEmpty(spec) && !spec.StartsWith('#'))
                {
                    PythonPackageInfo? pkg = ParsePackageSpec(spec, "setup.cfg");
                    if (pkg is not null)
                    {
                        if (inInstallRequires) deps.Add(pkg);
                        else devDeps.Add(pkg);
                    }
                }
            }
        }
    }

    private static PythonPackageInfo? ParsePackageSpec(string spec, string source)
    {
        spec = spec.Trim();
        if (string.IsNullOrEmpty(spec) || spec.StartsWith('#'))
            return null;

        // Strip environment markers: "package>=1.0; python_version>='3.8'"
        int markerIdx = spec.IndexOf(';');
        if (markerIdx >= 0)
            spec = spec[..markerIdx].Trim();

        // Find where the version spec starts
        int versionStart = -1;
        for (int i = 0; i < spec.Length; i++)
        {
            char c = spec[i];
            if (c is '>' or '<' or '=' or '!' or '~' or '^')
            {
                versionStart = i;
                break;
            }
        }

        string name;
        string? version;

        if (versionStart > 0)
        {
            name = spec[..versionStart].Trim();
            version = spec[versionStart..].Trim();
        }
        else
        {
            name = spec.Trim();
            version = null;
        }

        // Normalize name (PEP 508: replace _ and . with -)
        name = name.Replace('_', '-');

        if (string.IsNullOrEmpty(name))
            return null;

        return new PythonPackageInfo(name, version, source);
    }
}
