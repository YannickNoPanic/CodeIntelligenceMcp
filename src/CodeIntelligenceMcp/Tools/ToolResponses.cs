using System.Text.Encodings.Web;

namespace CodeIntelligenceMcp.Tools;

internal static class ToolResponses
{
    // Relaxed escaping: output goes to an LLM over stdio, never to HTML — escaping quotes
    // as ' would only waste tokens. WhenWritingNull drops empty optional fields.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string StaleHint = "index may be outdated — call refresh_workspace to rebuild";

    public static string Ok(object result, bool stale = false) =>
        stale
            ? JsonSerializer.Serialize(new { stale = true, hint = StaleHint, result }, JsonOptions)
            : JsonSerializer.Serialize(result, JsonOptions);

    public static string OkList<T>(IReadOnlyList<T> items, int maxResults = 100, bool stale = false, string? hint = null)
    {
        bool truncated = maxResults > 0 && items.Count > maxResults;
        var payload = new
        {
            total = items.Count,
            returned = truncated ? maxResults : items.Count,
            truncated,
            stale = stale ? (bool?)true : null,
            hint = hint ?? (truncated
                ? "truncated — refine the query or raise maxResults"
                : stale ? StaleHint : null),
            items = truncated ? items.Take(maxResults) : items
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string Err(string message, string? hint = null, IReadOnlyList<string>? detail = null) =>
        JsonSerializer.Serialize(new { error = message, hint, detail }, JsonOptions);
}
