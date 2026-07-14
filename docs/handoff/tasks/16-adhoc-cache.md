# Taak 16 — Ad-hoc pad hergebruikt cache van geconfigureerde workspace

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 16". **Audit-bevinding:** R10 —
dezelfde solution via naam ("datalake2") én via absoluut pad geeft twee
volledige onafhankelijke indexes (cache key = ws.Name): dubbel geheugen,
dubbele cold start.

## Bestand

`src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs`, de
`Path.IsPathRooted`-tak in `GetAsync` (regel ~27-31).

## Implementatie

```csharp
if (Path.IsPathRooted(workspace))
{
    string normalizedPath = workspace.Replace('\\', '/');
    WorkspaceConfig? configured = config.Workspaces.FirstOrDefault(w =>
        w.Type == workspaceType
        && GetConfiguredPath(w) is string p
        && string.Equals(p.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase));
    ws = configured ?? CreateAdHoc(normalizedPath);
}
```

Als het pad exact overeenkomt met de geconfigureerde solution/rootPath van een
workspace van dit type, gebruik dan die config — de cache key wordt dan de
workspacenaam en de bestaande index wordt hergebruikt.

`config` is de primary-constructorparameter (staat er al). Geen wijziging aan
`Invalidate`/`IsLoaded` nodig: die accepteren beide vormen al onafhankelijk,
en na deze fix landen naam- en pad-calls op dezelfde key.

## Acceptatie

- Bestaande tests groen. (Unittest optioneel: fake provider met een config
  waarvan het pad matcht — assert dat een tweede GetAsync met het pad geen
  tweede LoadAsync triggert. Kan met een tel-provider zoals in
  WorkspaceAccessTests.)
- Commit: `fix: absolute path to a configured solution reuses its index`
