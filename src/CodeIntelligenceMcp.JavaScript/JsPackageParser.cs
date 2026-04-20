using System.Text.Json;
using CodeIntelligenceMcp.JavaScript.Models;

namespace CodeIntelligenceMcp.JavaScript;

public static class JsPackageParser
{
    public static JsProjectInfo ParseProjectInfo(string rootPath)
    {
        string? projectName = null;
        string? version = null;
        var dependencies = new List<JsPackageInfo>();
        var scripts = new List<string>();
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string packageJsonPath = Path.Combine(rootPath, "package.json");
        if (File.Exists(packageJsonPath))
        {
            try
            {
                ParsePackageJson(packageJsonPath, ref projectName, ref version, dependencies, scripts, frameworks);
            }
            catch (Exception)
            {
                // Ignore parse errors
            }
        }

        return new JsProjectInfo(projectName, version, dependencies, scripts, [..frameworks]);
    }

    private static void ParsePackageJson(
        string path,
        ref string? projectName,
        ref string? version,
        List<JsPackageInfo> dependencies,
        List<string> scripts,
        HashSet<string> frameworks)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("name", out JsonElement nameEl))
            projectName = nameEl.GetString();

        if (root.TryGetProperty("version", out JsonElement versionEl))
            version = versionEl.GetString();

        // Production dependencies
        if (root.TryGetProperty("dependencies", out JsonElement depsEl))
        {
            foreach (JsonProperty prop in depsEl.EnumerateObject())
            {
                string pkgName = prop.Name;
                string? pkgVersion = prop.Value.GetString();
                dependencies.Add(new JsPackageInfo(pkgName, pkgVersion, false));
                DetectFramework(pkgName, frameworks);
            }
        }

        // Dev dependencies
        if (root.TryGetProperty("devDependencies", out JsonElement devDepsEl))
        {
            foreach (JsonProperty prop in devDepsEl.EnumerateObject())
            {
                string pkgName = prop.Name;
                string? pkgVersion = prop.Value.GetString();
                dependencies.Add(new JsPackageInfo(pkgName, pkgVersion, true));
                DetectFramework(pkgName, frameworks);
            }
        }

        // Scripts
        if (root.TryGetProperty("scripts", out JsonElement scriptsEl))
        {
            foreach (JsonProperty prop in scriptsEl.EnumerateObject())
                scripts.Add(prop.Name);
        }

        // Also detect from config files
        DetectFromConfigFiles(Path.GetDirectoryName(path)!, frameworks);
    }

    private static void DetectFromConfigFiles(string rootPath, HashSet<string> frameworks)
    {
        // nuxt.config.ts/js
        if (File.Exists(Path.Combine(rootPath, "nuxt.config.ts")) ||
            File.Exists(Path.Combine(rootPath, "nuxt.config.js")))
            frameworks.Add("Nuxt");

        // next.config.js/ts
        if (File.Exists(Path.Combine(rootPath, "next.config.js")) ||
            File.Exists(Path.Combine(rootPath, "next.config.ts")) ||
            File.Exists(Path.Combine(rootPath, "next.config.mjs")))
            frameworks.Add("Next.js");

        // vite.config.ts/js
        if (File.Exists(Path.Combine(rootPath, "vite.config.ts")) ||
            File.Exists(Path.Combine(rootPath, "vite.config.js")))
            frameworks.Add("Vite");

        // tsconfig
        if (File.Exists(Path.Combine(rootPath, "tsconfig.json")))
            frameworks.Add("TypeScript");

        // pages/ directory = Nuxt or Next
        if (Directory.Exists(Path.Combine(rootPath, "pages")))
        {
            if (!frameworks.Contains("Next.js"))
                frameworks.Add("Nuxt");
        }
    }

    private static void DetectFramework(string packageName, HashSet<string> frameworks)
    {
        switch (packageName.ToLowerInvariant())
        {
            case "vue" or "@vue/core": frameworks.Add("Vue"); break;
            case "nuxt": frameworks.Add("Nuxt"); break;
            case "react": frameworks.Add("React"); break;
            case "next": frameworks.Add("Next.js"); break;
            case "svelte": frameworks.Add("Svelte"); break;
            case "express": frameworks.Add("Express"); break;
            case "fastify": frameworks.Add("Fastify"); break;
            case "pinia" or "@pinia/nuxt": frameworks.Add("Pinia"); break;
            case "vite": frameworks.Add("Vite"); break;
            case "typescript": frameworks.Add("TypeScript"); break;
            case "@nestjs/core": frameworks.Add("NestJS"); break;
            case "@tanstack/vue-query" or "@tanstack/react-query": frameworks.Add("TanStack Query"); break;
        }
    }
}
