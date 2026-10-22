using Microsoft.AspNetCore.Mvc;
using System.Text;
namespace SplunkAPI.Endpoints;

public sealed class ApiTokenRequiredMetadata;

public static class Search
{
    private static readonly string[] Supported = ["csv", "json", "json_cols", "json_rows", "raw", "xml"];

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/service/search", HandleSearchAsync)
            .WithMetadata(new ApiTokenRequiredMetadata())
            .WithName("Search")
            .WithSummary("Execute an SPL search")
            .WithDescription("Executes SPL using the Splunk Management REST API.")
            .WithTags("Splunk")
            .Accepts<string>("text/plain")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status504GatewayTimeout);
        return endpoints;
    }

    private static async Task<IResult> HandleSearchAsync(HttpContext context, [FromQuery] string? format, [FromQuery] string? earliest, [FromQuery] string? latest, IManagementClient splunk, Options options, CancellationToken cancellationToken)
    {
        string query;
        using (StreamReader reader = new(context.Request.Body, Encoding.UTF8))
            query = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
            return Results.BadRequest(new { error = "The request body must contain an SPL query." });
        if (string.IsNullOrEmpty(earliest))
            earliest = null;
        if (string.IsNullOrEmpty(latest))
            latest = null;
        format = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim();
        if (!SearchUtility.IsSupportedOutputMode(format))
            return Results.BadRequest(new { error = "Unsupported format.", supported = Supported });
        try
        {
            SearchResult result = await splunk.SearchAsync(query, earliest, latest, format, options.SearchMaxResults, cancellationToken);
            return Results.Content(result.Content, result.ContentType, Encoding.UTF8, StatusCodes.Status200OK);
        }
        catch (TimeoutException ex)
        {
            return Results.Problem(title: "Splunk search timed out", detail: ex.Message, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (SplunkApiException ex)
        {
            return Results.Problem(title: "Splunk search failed", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}