using System.Text.Json;
namespace SplunkAPI;

public interface IManagementClient
{
    Task<SearchResult> SearchAsync(string query, string? earliest, string? latest, string outputMode, int maxResults, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetIndexesAsync(CancellationToken cancellationToken = default);

    Task<string> GetServerInfoAsync(CancellationToken cancellationToken = default);
}

public sealed class ManagementClient(HttpClient http, Options options) : IManagementClient
{
    private readonly HttpClient Http = http;
    private readonly Options Options = options;

    public async Task<SearchResult> SearchAsync(string query, string? earliest, string? latest, string outputMode, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("The SPL query must not be empty.", nameof(query));
        if (!SearchUtility.IsSupportedOutputMode(outputMode))
            throw new ArgumentException($"Unsupported output mode '{outputMode}'.", nameof(outputMode));
        maxResults = Math.Clamp(maxResults, 1, Options.SearchMaxResults);
        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(Options.SearchMaxTimeSeconds));
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        CancellationToken ct = linkedCts.Token;
        string? sid = null;
        try
        {
            sid = await CreateSearchJobAsync(query, earliest, latest, ct);
            await WaitForCompletionAsync(sid, ct);
            string content = await GetResultsAsync(sid, outputMode, maxResults, ct);
            return new(sid, outputMode, SearchUtility.ContentTypeFor(outputMode), content);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Splunk search exceeded the configured timeout of {Options.SearchMaxTimeSeconds} seconds.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sid))
                await DeleteJobBestEffortAsync(sid);
        }
    }

    public async Task<IReadOnlyList<string>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Http.GetAsync("services/data/indexes?output_mode=json&count=0", cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "list Splunk indexes");
        using JsonDocument document = JsonDocument.Parse(body);
        List<string> result = [];
        if (document.RootElement.TryGetProperty("entry", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                {
                    string? value = name.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value);
                }
            }
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public async Task<string> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Http.GetAsync("services/server/info?output_mode=json", cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "get Splunk server info");
        return body;
    }

    private async Task<string> CreateSearchJobAsync(string query, string? earliest, string? latest, CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> values =
        [
            new("search", SearchUtility.NormalizeSearch(query)),
            new("exec_mode", "normal"),
            new("output_mode", "json")
        ];
        if (!string.IsNullOrWhiteSpace(earliest))
            values.Add(new("earliest_time", earliest));
        if (!string.IsNullOrWhiteSpace(latest))
            values.Add(new("latest_time", latest));
        using FormUrlEncodedContent content = new(values);
        using HttpResponseMessage response = await Http.PostAsync("services/search/jobs", content, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "create Splunk search job");
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            string? sid = document.RootElement.GetProperty("sid").GetString();
            if (string.IsNullOrWhiteSpace(sid))
                throw new SplunkApiException("Splunk created a search job but returned an empty SID.");
            return sid;
        }
        catch (JsonException ex)
        {
            throw new SplunkApiException("Splunk returned an invalid JSON response while creating a search job.", innerException: ex);
        }
    }

    private async Task WaitForCompletionAsync(string sid, CancellationToken cancellationToken)
    {
        string encodedSid = Uri.EscapeDataString(sid);
        while (true)
        {
            using HttpResponseMessage response = await Http.GetAsync($"services/search/jobs/{encodedSid}?output_mode=json", cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, body, "poll Splunk search job");
            string state = ReadDispatchState(body);
            switch (state.ToUpperInvariant())
            {
                case "DONE":
                    return;
                case "FAILED":
                    throw new SplunkApiException($"Splunk search job {sid} failed.");
                default:
                    await Task.Delay(Options.SearchPollIntervalMilliseconds, cancellationToken);
                    break;
            }
        }
    }

    private async Task<string> GetResultsAsync(string sid, string outputMode, int maxResults, CancellationToken cancellationToken)
    {
        string encodedSid = Uri.EscapeDataString(sid);
        string encodedOutputMode = Uri.EscapeDataString(outputMode);
        string url = $"services/search/v2/jobs/{encodedSid}/results?output_mode={encodedOutputMode}&count={maxResults}";
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "get Splunk search results");
        return body;
    }

    private static string ReadDispatchState(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement entries = document.RootElement.GetProperty("entry");
            if (entries.GetArrayLength() == 0)
                throw new SplunkApiException("Splunk search status response did not contain an entry.");
            JsonElement content = entries[0].GetProperty("content");
            string? state = content.GetProperty("dispatchState").GetString();
            return state ?? string.Empty;
        }
        catch (JsonException ex)
        {
            throw new SplunkApiException("Splunk returned an invalid JSON search status response.", innerException: ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new SplunkApiException("Splunk search status response was missing dispatchState.", innerException: ex);
        }
    }

    private async Task DeleteJobBestEffortAsync(string sid)
    {
        try
        {
            using CancellationTokenSource cleanupCts = new(TimeSpan.FromSeconds(3));
            string encodedSid = Uri.EscapeDataString(sid);
            using HttpResponseMessage response = await Http.DeleteAsync($"services/search/jobs/{encodedSid}", cleanupCts.Token);
            _ = response.StatusCode;
        }
        catch { }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;
        string message = string.IsNullOrWhiteSpace(body) ? $"Splunk failed to {operation}: HTTP {(int)response.StatusCode} {response.StatusCode}." : $"Splunk failed to {operation}: HTTP {(int)response.StatusCode} {response.StatusCode}. Response: {Limit(body, 2000)}";
        throw new SplunkApiException(message, (int)response.StatusCode);
    }

    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}