namespace SplunkAPI;

public partial class SearchUtility
{
    private static readonly HashSet<string> SupportedOutputModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv",
        "json",
        "json_cols",
        "json_rows",
        "raw",
        "xml"
    };

    public static bool IsSupportedOutputMode(string outputMode) => SupportedOutputModes.Contains(outputMode);

    public static string EscapeSplString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    public static string NormalizeSearch(string query)
    {
        query = query.Trim();
        if (query.StartsWith('|') || query.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
            return query;
        return "search " + query;
    }

    public static string ContentTypeFor(string outputMode) => outputMode.ToLowerInvariant() switch
    {
        "csv" => "text/csv; charset=utf-8",
        "json" or "json_cols" or "json_rows" => "application/json; charset=utf-8",
        "xml" => "application/xml; charset=utf-8",
        "raw" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };
}