namespace SplunkAPI;

public sealed class SplunkApiException(string message, int? statusCode = null, Exception? innerException = null) : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
}