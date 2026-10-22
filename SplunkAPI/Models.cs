namespace SplunkAPI;

public record SearchResult(string Sid, string OutputMode, string ContentType, string Content);
public record HttpResult(int StatusCode, string ContentType, string Content);