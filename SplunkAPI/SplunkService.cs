using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using GenHTTP.Api.Protocol;
using GenHTTP.Modules.Reflection;
using GenHTTP.Modules.Webservices;
namespace SplunkAPI;
using static Helpers;

[WebService("service")]
public class SplunkService
{
    [ResourceMethod(RequestMethod.Post, "search")]
    public async Task<IResponse> Search(IRequest request, string token, string format, string? earliest, string? latest, [FromBody] string search)
    {
        try
        {
            (bool success, string content) = await SearchRunner.RunAsync(search, token, format, earliest, latest);
            if (string.IsNullOrWhiteSpace(content))
            {
                content = "Splunk search failed";
                success = false;
            }
            return Ack(request, success ? ResponseStatus.Ok : ResponseStatus.BadGateway, content);
        }
        catch (ArgumentException ex)
        {
            return Ack(request, ResponseStatus.BadRequest, ex.Message);
        }
        catch (TimeoutException ex)
        {
            return Ack(request, ResponseStatus.GatewayTimeout, ex.Message);
        }
        catch (Exception ex)
        {
            return Ack(request, ResponseStatus.InternalServerError, ex.Message);
        }
    }

    [ResourceMethod(RequestMethod.Post, "ingest")]
    public async Task<IResponse> Ingest(IRequest request, string token, string? index, string? sourcetype, double? time, [FromBody] JsonElement data)
    {
        if (!EnsureToken(token))
            return Ack(request, ResponseStatus.BadRequest, "Invalid HEC token");
        if (data.ValueKind == JsonValueKind.Undefined || data.ValueKind == JsonValueKind.Null)
            return Ack(request, ResponseStatus.BadRequest, "Invalid event");
        HecEvent evt = new()
        { 
            Event = data.Clone(),
            Index = EnsureValue(index),
            Sourcetype = EnsureValue(sourcetype),
            Time = time
        };
        HttpRequestMessage msg = new(HttpMethod.Post, SplunkHecUrl) { Content = JsonContent.Create(evt, options: JsonOptions) };
        msg.Headers.Authorization = new("Splunk", token);
        try
        {
            using HttpResponseMessage response = await Client.SendAsync(msg);
            string body = await response.Content.ReadAsStringAsync();
            return Ack(request, CastStatus(response.StatusCode), body);
        }
        catch (TaskCanceledException)
        {
            return Ack(request, ResponseStatus.GatewayTimeout, "Splunk HEC request timed out");
        }
        catch (HttpRequestException)
        {
            return Ack(request, ResponseStatus.BadGateway, "Splunk HEC endpoint unreachable");
        }
        catch (Exception ex)
        {
            return Ack(request, ResponseStatus.InternalServerError, ex.Message);
        }
    }
}