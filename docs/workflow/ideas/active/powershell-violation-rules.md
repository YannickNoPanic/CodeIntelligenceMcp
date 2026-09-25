# PowerShell violation rules

id: powershell-violation-rules
type: idea
status: active
created: 2026-09-25
expires: 2026-12-31
summary: Add violation rules for powershell workspaces, mirroring the Roslyn find_violations/scan_all_violations experience.

## Idea

Violation rules today are Roslyn-only. Candidate PowerShell rules:

- `ps-unapproved-verb` - function name uses a non-approved verb (parser already has `KnownVerbPrefixes`)
- `ps-no-cmdletbinding` - exported function without `[CmdletBinding()]`
- `ps-alias-usage` - aliases (`%`, `?`, `gci`, `ls`) instead of full cmdlet names
- `ps-plaintext-password` - `ConvertTo-SecureString -AsPlainText` or `[string]$Password`
- `ps-invoke-expression` - `Invoke-Expression` usage
- `ps-manifest-export-wildcard` - `FunctionsToExport = '*'` in `.psd1`
- `ps-global-scope` - `$global:` assignments
- `ps-empty-catch` - empty `catch` blocks

## Why It Might Matter

PowerShell workspaces only get structural lookups; a health check in the same compact envelope as the .NET rules would make reviews of script repos cheap.

## Validation Needed

- Tool shape: extend `find_violations`/`scan_all_violations` by workspace type, or add `ps_find_violations`.
- Overlap with PSScriptAnalyzer: is an in-MCP subset worth it versus shelling out?
- Which rules fire usefully on a real script repo (false-positive rate).
