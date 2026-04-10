using CodeIntelligenceMcp.JavaScript.Models;

namespace CodeIntelligenceMcp.JavaScript.Models;

public sealed record VueSfcBlock(
    string Tag,         // "template", "script", "style"
    string? Lang,       // "ts", "scss", "pug", null for default
    bool IsSetup,       // <script setup>
    int LineStart,
    int LineEnd,
    string Content);

public sealed record VueSfcInfo(
    string FilePath,
    IReadOnlyList<VueSfcBlock> Blocks,
    JsFileInfo? ScriptAnalysis,
    IReadOnlyList<string> Props,
    IReadOnlyList<string> Emits,
    IReadOnlyList<string> Composables);
