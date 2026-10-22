using Microsoft.AspNetCore.Mvc;
using System.Text;
namespace SplunkAPI.Endpoints;

public static class Ingest
{
    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/service/ingest", HandleIngestAsync)
            .WithName("Ingest")
            .WithSummary("Send an event to Splunk HEC")
            .WithDescription("Forwards an event to Splunk HEC. The HEC token is supplied using the token query parameter.")
            .WithTags("Splunk")
            .Accepts<object>("application/json")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway);
        return endpoints;
    }

    private static async Task<IResult> HandleIngestAsync(HttpContext context, [FromQuery] string token, [FromQuery] string index, [FromQuery] string sourcetype, IHecClient hec, Options options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(sourcetype))
            return Results.BadRequest(new { error = "Query parameters token, index and sourcetype are required." });
        if (context.Request.ContentLength > 0 && context.Request.ContentLength > options.IngestMaxBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        string rawBody;
        using (StreamReader reader = new(context.Request.Body, Encoding.UTF8))
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(rawBody) > options.IngestMaxBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (string.IsNullOrWhiteSpace(rawBody))
            return Results.BadRequest(new { error = "The request body must contain a JSON event." });
        try
        {
            HttpResult result = await hec.IngestAsync(token, index, sourcetype, rawBody, cancellationToken);
            return Results.Content(result.Content, result.ContentType, Encoding.UTF8, result.StatusCode);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(title: "Splunk HEC request failed", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}