using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
namespace SplunkAPI.Endpoints;

[McpServerToolType]
public class McpTools
{
    [McpServerTool(Name = "splunk_search")]
    [Description("Runs a read-only SPL query against Splunk and returns JSON results. Use this for flexible analysis. Mutating or high-risk SPL commands are rejected.")]
    public static async Task<string> SearchAsync(
        IManagementClient splunk,
        QueryGuard guard,
        Options options,
        [Description("The SPL query. Both 'index=...' and full 'search index=...' forms are accepted.")] string query,
        [Description("Earliest Splunk time, for example -24h, -30d@d or 0.")] string earliest = "-24h",
        [Description("Latest Splunk time, usually now.")] string latest = "now",
        [Description("Maximum number of result rows. The server-wide SPLUNK_SEARCH_MAXOUT limit is always enforced.")] int maxResults = 500,
        CancellationToken cancellationToken = default)
    {
        guard.EnsureMcpQueryIsReadOnly(query);
        maxResults = Math.Clamp(maxResults, 1, options.SearchMaxResults);
        SearchResult result = await splunk.SearchAsync(query, earliest, latest, "json", maxResults, cancellationToken);
        return result.Content;
    }

    [McpServerTool(Name = "splunk_get_indexes")]
    [Description("Lists the indexes known by the connected Splunk instance.")]
    public static async Task<string> GetIndexesAsync(
        IManagementClient splunk,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> indexes = await splunk.GetIndexesAsync(cancellationToken);
        return JsonSerializer.Serialize(indexes);
    }

    [McpServerTool(Name = "splunk_get_server_info")]
    [Description("Returns Splunk server information, including version and license information exposed by /services/server/info.")]
    public static Task<string> GetServerInfoAsync(
        IManagementClient splunk,
        CancellationToken cancellationToken = default) =>
        splunk.GetServerInfoAsync(cancellationToken);

    [McpServerTool(Name = "splunk_get_sourcetypes")]
    [Description("Lists sourcetypes found in an index, including event count and first/last event times.")]
    public static async Task<string> GetSourcetypesAsync(
        IManagementClient splunk,
        [Description("Splunk index name.")] string index,
        CancellationToken cancellationToken = default)
    {
        string safeIndex = SearchUtility.EscapeSplString(index);
        string query = $"| metadata type=sourcetypes index=\"{safeIndex}\" " +
                    "| convert ctime(firstTime) ctime(lastTime) " +
                    "| table sourcetype totalCount firstTime lastTime " +
                    "| sort sourcetype";
        SearchResult result = await splunk.SearchAsync(query, null, null, "json", 2000, cancellationToken);
        return result.Content;
    }

    [McpServerTool(Name = "splunk_get_fields")]
    [Description("Samples events from an index/sourcetype and summarizes discovered fields. This is useful for understanding an unfamiliar schema.")]
    public static async Task<string> GetFieldsAsync(
        IManagementClient splunk,
        [Description("Splunk index name.")] string index,
        [Description("Splunk sourcetype name.")] string sourcetype,
        [Description("Earliest time used for the schema sample.")] string earliest = "-30d",
        [Description("Latest time used for the schema sample.")] string latest = "now",
        [Description("Number of events to sample before fieldsummary.")] int sampleSize = 100,
        CancellationToken cancellationToken = default)
    {
        sampleSize = Math.Clamp(sampleSize, 1, 1000);
        string safeIndex = SearchUtility.EscapeSplString(index);
        string safeSourcetype = SearchUtility.EscapeSplString(sourcetype);
        string query = $"search index=\"{safeIndex}\" sourcetype=\"{safeSourcetype}\" " +
                    $"| head {sampleSize} " +
                    "| fieldsummary " +
                    "| fields field count distinct_count is_exact min max mean " +
                    "| sort field";
        SearchResult result = await splunk.SearchAsync(query, earliest, latest, "json", 5000, cancellationToken);
        return result.Content;
    }

    [McpServerTool(Name = "splunk_sample_events")]
    [Description("Returns a small sample of raw events for an index and sourcetype.")]
    public static async Task<string> SampleEventsAsync(
        IManagementClient splunk,
        [Description("Splunk index name.")] string index,
        [Description("Splunk sourcetype name.")] string sourcetype,
        [Description("Number of events to return.")] int count = 5,
        [Description("Earliest time for the sample.")] string earliest = "-24h",
        [Description("Latest time for the sample.")] string latest = "now",
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 100);
        string safeIndex = SearchUtility.EscapeSplString(index);
        string safeSourcetype = SearchUtility.EscapeSplString(sourcetype);
        string query = $"search index=\"{safeIndex}\" sourcetype=\"{safeSourcetype}\" | head {count}";
        SearchResult result = await splunk.SearchAsync(query, earliest, latest, "json", count, cancellationToken);
        return result.Content;
    }
}