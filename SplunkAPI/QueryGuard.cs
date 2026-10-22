using System.Text.RegularExpressions;
namespace SplunkAPI;

public partial class QueryGuard(Options options)
{
    private readonly Options Options = options;

    [GeneratedRegex(@"(?im)(?:^|\|)\s*(delete|collect|mcollect|meventcollect|outputlookup|outputcsv|sendemail|sendalert|script|runshellscript|dbxquery|rest)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DisallowedCommandRegex();

    public void EnsureMcpQueryIsReadOnly(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("The SPL query must not be empty.", nameof(query));
        if (query.Length > Options.McpMaxQueryLength)
            throw new ArgumentException($"The SPL query exceeds the configured MCP query length limit of {Options.McpMaxQueryLength} characters.", nameof(query));
        Match match = DisallowedCommandRegex().Match(query);
        if (match.Success)
            throw new InvalidOperationException($"The MCP search tool is read-only. SPL command '{match.Groups[1].Value}' is not allowed.");
    }
}