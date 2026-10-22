using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SplunkAPI;

public interface IHecClient
{
    Task<HttpResult> IngestAsync(string hecToken, string index, string sourcetype, string rawJsonEvent, CancellationToken cancellationToken = default);
}

public class HecClient(HttpClient http, Options options) : IHecClient
{
    private readonly HttpClient Http = http;
    private readonly Options Options = options;

    public async Task<HttpResult> IngestAsync(string hecToken, string index, string sourcetype, string rawJsonEvent, CancellationToken cancellationToken = default)
    {
        JsonNode? eventNode;
        try
        {
            eventNode = JsonNode.Parse(rawJsonEvent);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new ArgumentException("The ingest request body must be valid JSON.", nameof(rawJsonEvent), ex);
        }
        JsonObject payload = new()
        {
            ["event"] = eventNode,
            ["index"] = index,
            ["sourcetype"] = sourcetype
        };
        using HttpRequestMessage request = new(HttpMethod.Post, Options.HecUrl) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Splunk", hecToken);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8", body);
    }
}