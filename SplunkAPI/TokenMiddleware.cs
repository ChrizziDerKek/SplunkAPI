using System.Security.Cryptography;
using System.Text;
namespace SplunkAPI;

public class TokenMiddleware(RequestDelegate next, Options options)
{
    private readonly RequestDelegate Next = next;
    private readonly Options Options = options;

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path;
        if (path.StartsWith("/service/ingest") || path.StartsWith("/swagger") || path.StartsWith("/mcp") || path.StartsWith("/.well-known") || path.StartsWith("/authorize") || path.StartsWith("/token"))
        {
            await Next(context);
            return;
        }
        string? supplied = GetSuppliedToken(context.Request);
        if (!FixedTimeEquals(Options.ApiToken, supplied))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }
        await Next(context);
    }

    private static string? GetSuppliedToken(HttpRequest request)
    {
        string? authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization[7..].Trim();
        string? headerToken = request.Headers["X-Splunk-Api-Token"].ToString();
        if (!string.IsNullOrWhiteSpace(headerToken))
            return headerToken.Trim();
        string? queryToken = request.Query["token"].ToString();
        return string.IsNullOrWhiteSpace(queryToken) ? null : queryToken;
    }

    private static bool FixedTimeEquals(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(supplied))
            return false;
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}