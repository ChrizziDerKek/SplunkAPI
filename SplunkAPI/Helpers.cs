using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using GenHTTP.Api.Protocol;
using GenHTTP.Modules.IO;
namespace SplunkAPI;

static class Helpers
{
    public static readonly HttpClient Client = new(CreateHandler()) { Timeout = TimeSpan.FromSeconds(20) };
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static readonly Uri SplunkHecUrl = GetSplunkHecUrl();

    /// <summary>
    /// Sends an api response
    /// </summary>
    /// <param name="request">Source request</param>
    /// <param name="status">Response status</param>
    /// <param name="content">Optional content</param>
    /// <returns>Response object</returns>
    public static IResponse Ack(IRequest request, ResponseStatus status, string? content = null) => request.Respond().Status(status).Content(content ?? "").Build();

    /// <summary>
    /// Checks if a splunk hec token is valid
    /// </summary>
    /// <param name="token">Token to check</param>
    /// <returns>True on success</returns>
    public static bool EnsureToken(string? token) => !string.IsNullOrWhiteSpace(token) && !token.Any(char.IsWhiteSpace) && !token.Any(char.IsControl);

    /// <summary>
    /// Checks if a string has a value
    /// </summary>
    /// <param name="value">Value to check</param>
    /// <returns>String value on success, otherwise null</returns>
    public static string? EnsureValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Converts a http status code to a genhttp status code
    /// </summary>
    /// <param name="status">Http status code</param>
    /// <returns>Genhttp status code</returns>
    public static ResponseStatus CastStatus(HttpStatusCode status)
    {
        int code = (int)status;
        if (Enum.IsDefined(typeof(ResponseStatus), code))
            return (ResponseStatus)code;
        return code >= 200 && code <= 299 ? ResponseStatus.Ok : ResponseStatus.BadGateway;
    }

    /// <summary>
    /// Dirty way to get environment variables
    /// </summary>
    /// <param name="variable">Environment variable to get</param>
    /// <param name="fallback">Fallback value</param>
    /// <returns>Environment variable value on success, otherwise fallback</returns>
    public static int GetEnvironmentVariableInt(string variable, int fallback)
    {
        string value = GetEnvironmentVariableString(variable, "");
        return string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out int result) ? fallback : result;
    }

    /// <summary>
    /// Dirty way to get environment variables
    /// </summary>
    /// <param name="variable">Environment variable to get</param>
    /// <param name="fallback">Fallback value</param>
    /// <returns>Environment variable value on success, otherwise fallback</returns>
    public static bool GetEnvironmentVariableBool(string variable, bool fallback)
    {
        string value = GetEnvironmentVariableString(variable, "");
        return string.IsNullOrWhiteSpace(value) || !bool.TryParse(value, out bool result) ? fallback : result;
    }

    /// <summary>
    /// Dirty way to get environment variables
    /// </summary>
    /// <param name="variable">Environment variable to get</param>
    /// <param name="fallback">Fallback value</param>
    /// <returns>Environment variable value on success, otherwise fallback</returns>
    public static string GetEnvironmentVariableString(string variable, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        string dir = Environment.CurrentDirectory;
        for (int i = 0; i < 5; i++)
        {
            string file = Path.Combine(dir, ".env");
            if (File.Exists(file))
            {
                foreach (string line in File.ReadAllLines(file).Where(x => x.Contains('=')))
                {
                    string[] slices = [.. line.Split('=').Select(x => x.Trim())];
                    if (slices.Length < 2 || slices[0] != variable)
                        continue;
                    return slices[1];
                }
                break;
            }
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        return fallback;
    }

    /// <summary>
    /// Retrieves the configured splunk hec url
    /// </summary>
    /// <returns>Splunk hec url</returns>
    /// <exception cref="InvalidOperationException">Splunk hec url is missing or invalid</exception>
    private static Uri GetSplunkHecUrl()
    {
        string value = GetEnvironmentVariableString("SPLUNK_HEC_URL", "");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Missing SPLUNK_HEC_URL in environment");
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            throw new InvalidOperationException("SPLUNK_HEC_URL isn't a valid URL");
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new InvalidOperationException("SPLUNK_HEC_URL doesn't use HTTP or HTTPS");
        return uri;
    }

    /// <summary>
    /// Creates a socket handler that can ignore https certificate errors if configured
    /// </summary>
    /// <returns>Socket handler</returns>
    private static SocketsHttpHandler CreateHandler()
    {
        SocketsHttpHandler handler = new() { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5) };
        if (GetEnvironmentVariableBool("SPLUNK_HEC_IGNORE_CERT_ERRORS", false))
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        return handler;
    }
}