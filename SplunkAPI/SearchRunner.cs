using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
namespace SplunkAPI;
using static Helpers;

static class SearchRunner
{
    public static async Task<(bool Success, string Content)> RunAsync(string search, string token, string format, string? earliest, string? latest, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search))
            throw new ArgumentException("Invalid search");
        search = search.Replace("\n", " ");
        if (format != "json" && format != "csv" && format != "table" && format != "raw" && format != "rawdata")
            throw new ArgumentException("Invalid format (json / csv / table / raw / rawdata)");
        if (!GetEnvironmentVariableString("SPLUNK_SEARCH_API_TOKEN", "").Equals(token, StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("Failed to authenticate");
        int maxout = GetEnvironmentVariableInt("SPLUNK_SEARCH_MAXOUT", 1000);
        int maxtime = GetEnvironmentVariableInt("SPLUNK_SEARCH_MAXTIME", 20);
        bool preview = GetEnvironmentVariableBool("SPLUNK_SEARCH_PREVIEW", false);
        string exe = GetEnvironmentVariableString("SPLUNK_EXECUTABLE", "");
        List<string> arguments = [
            "search",
            search.Trim(),
            "-preview",
            preview ? "true" : "false",
            "-maxout",
            maxout.ToString(CultureInfo.InvariantCulture),
            "-maxtime",
            maxtime.ToString(CultureInfo.InvariantCulture),
            "-output",
            format.Trim()
        ];
        if (!string.IsNullOrWhiteSpace(earliest))
            arguments.AddRange(["-earliest_time", earliest]);
        if (!string.IsNullOrWhiteSpace(latest))
            arguments.AddRange(["-earliest_time", latest]);
        ProcessStartInfo info = new()
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (!GetEnvironmentVariableBool("SPLUNK_RUNNING_IN_KUBERNETES", false))
            info.FileName = exe;
        else
        {
            info.FileName = "kubectl";
            info.ArgumentList.Add("exec");
            info.ArgumentList.Add("-n");
            info.ArgumentList.Add(GetEnvironmentVariableString("SPLUNK_KUBERNETES_NAMESPACE", "default"));
            info.ArgumentList.Add(GetEnvironmentVariableString("SPLUNK_KUBERNETES_POD", "splunk-0"));
            string container = GetEnvironmentVariableString("SPLUNK_KUBERNETES_POD_CONTAINER", "");
            if (!string.IsNullOrWhiteSpace(container))
            {
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add(container);
            }
            info.ArgumentList.Add("--");
            info.ArgumentList.Add(exe);
        }
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        Process process = new() { StartInfo = info };
        if (!process.Start())
            throw new InvalidOperationException("Cannot start process");
        process.StandardInput.Close();
        Task<string> outputtask = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> errortask = process.StandardError.ReadToEndAsync(ct);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
            if (ct.IsCancellationRequested)
                throw;
            throw new TimeoutException("Splunk process timed out");
        }
        string output = await outputtask;
        string error = await errortask;
        return (process.ExitCode == 0, process.ExitCode == 0 ? output : error);
    }
}