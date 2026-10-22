namespace SplunkAPI;

public class Options
{
    public required string HecUrl { get; init; }
    public bool HecIgnoreCertificateErrors { get; init; }

    public required string ManagementUrl { get; init; }
    public bool ManagementIgnoreCertificateErrors { get; init; }

    public required string ApiToken { get; init; }
    public bool UsesLegacySearchApiToken { get; init; }

    public int SearchMaxResults { get; init; } = 5000;
    public int SearchMaxTimeSeconds { get; init; } = 60;
    public int SearchPollIntervalMilliseconds { get; init; } = 250;
    public int IngestMaxBytes { get; init; } = 1048576;
    public int McpMaxQueryLength { get; init; } = 20000;

    public static Options FromEnvironment(string file = ".env")
    {
        LoadEnvFile(file);
        string? apiToken = Environment.GetEnvironmentVariable("SPLUNK_API_TOKEN");
        string? legacyToken = Environment.GetEnvironmentVariable("SPLUNK_SEARCH_API_TOKEN");
        bool usesLegacyToken = string.IsNullOrWhiteSpace(apiToken) && !string.IsNullOrWhiteSpace(legacyToken);
        apiToken ??= legacyToken;
        if (string.IsNullOrWhiteSpace(apiToken))
            throw new InvalidOperationException("SPLUNK_API_TOKEN is required. SPLUNK_SEARCH_API_TOKEN is accepted as a backwards-compatible fallback.");
        return new()
        {
            HecUrl = Required("SPLUNK_HEC_URL"),
            HecIgnoreCertificateErrors = Bool("SPLUNK_HEC_IGNORE_CERT_ERRORS", false),
            ManagementUrl = Required("SPLUNK_MANAGEMENT_URL"),
            ManagementIgnoreCertificateErrors = Bool("SPLUNK_MANAGEMENT_IGNORE_CERT_ERRORS", false),
            ApiToken = apiToken,
            UsesLegacySearchApiToken = usesLegacyToken,
            SearchMaxResults = Int("SPLUNK_SEARCH_MAXOUT", 5000, 1, 100000),
            SearchMaxTimeSeconds = Int("SPLUNK_SEARCH_MAXTIME", 60, 1, 3600),
            SearchPollIntervalMilliseconds = Int("SPLUNK_SEARCH_POLL_INTERVAL_MS", 250, 50, 5000),
            IngestMaxBytes = Int("SPLUNK_INGEST_MAX_BYTES", 1048576, 1024, 100 * 1024 * 1024),
            McpMaxQueryLength = Int("SPLUNK_MCP_MAX_QUERY_LENGTH", 20000, 100, 1000000)
        };
    }

    private static void LoadEnvFile(string file)
    {
        string alternative = file + ".example";
        if (File.Exists(alternative))
            file = alternative;
        if (!File.Exists(file))
            return;
        foreach (string line in File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Contains('='))
                continue;
            string[] parts = [.. line.Trim().Split('=').Select(x => x.Trim())];
            if (parts.Length != 2)
                continue;
            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }

    private static string Required(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        return value.Trim();
    }

    private static bool Bool(string name, bool fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw) ? fallback : bool.Parse(raw);
    }

    private static int Int(string name, int fallback, int min, int max)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw, out int value) || value < min || value > max)
            throw new InvalidOperationException($"{name} must be an integer between {min} and {max}.");
        return value;
    }
}