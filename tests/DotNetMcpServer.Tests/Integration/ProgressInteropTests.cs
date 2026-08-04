using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives a long-running tool with a progress token and asserts the client sees the work
/// advance, against the shipped server as a real subprocess.
/// </summary>
public sealed class ProgressInteropTests : IAsyncLifetime
{
    private const int DocumentCount = 12;

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-progress-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);

        for (var index = 0; index < DocumentCount; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_workspace, $"note-{index:D2}.md"),
                $"# Note {index}\nLine one.\nLine two.\n");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private McpClient Client => _client ?? throw new InvalidOperationException("Client was not initialised.");

    [Fact]
    public async Task A_long_running_tool_reports_progress_as_it_goes()
    {
        var progress = new CollectingProgress();

        var result = await Client.CallToolAsync("scan_workspace", progress: progress);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains($"\"documents\":{DocumentCount}", TextOf(result), StringComparison.Ordinal);

        // One report short is expected, not flaky. The final report is issued immediately
        // before the tool returns and the response overtakes it: the SDK stops routing
        // notifications for a request once that request has been answered.
        await WaitForAsync(
            () => progress.Reports.Count >= DocumentCount - 1,
            () => $"reports={progress.Reports.Count}");

        var reports = progress.Reports;

        Assert.All(reports, report => Assert.Equal(DocumentCount, report.Total));
        Assert.All(reports, report => Assert.StartsWith("note-", report.Message, StringComparison.Ordinal));

        // Arrival order is not asserted: the reports are dispatched concurrently on the client
        // side. What the server owes is a distinct, in-range step for each document it walked.
        var steps = reports.Select(report => report.Progress).ToList();
        Assert.Equal(steps.Count, steps.Distinct().Count());
        Assert.All(steps, step => Assert.InRange(step, 1, DocumentCount));
    }

    [Fact]
    public async Task The_same_tool_runs_without_a_progress_token()
    {
        // Progress is the client's choice. A tool that only works when someone is watching is
        // a tool that fails in every other client.
        var result = await Client.CallToolAsync("scan_workspace");

        Assert.NotEqual(true, result.IsError);
        Assert.Contains($"\"documents\":{DocumentCount}", TextOf(result), StringComparison.Ordinal);
    }

    /// <remarks>
    /// Generous on purpose. The SDK dispatches notification handlers without waiting for them,
    /// so the reports can still be running when the tool call returns — and the whole suite
    /// runs several server subprocesses at once, which makes that gap wider than it looks on
    /// an idle machine.
    /// </remarks>
    private static async Task WaitForAsync(Func<bool> condition, Func<string> describe)
    {
        for (var attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.True(condition(), $"The expected progress reports did not arrive: {describe()}");
    }

    /// <summary>
    /// Collects reports without <see cref="Progress{T}"/>'s re-dispatch, which would add a
    /// second source of reordering on top of the one being measured — and would append to a
    /// list from several thread-pool threads at once.
    /// </summary>
    private sealed class CollectingProgress : IProgress<ProgressNotificationValue>
    {
        private readonly Lock _gate = new();
        private readonly List<ProgressNotificationValue> _reports = [];

        public IReadOnlyList<ProgressNotificationValue> Reports
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(ProgressNotificationValue value)
        {
            lock (_gate)
            {
                _reports.Add(value);
            }
        }
    }

    private static string TextOf(CallToolResult result)
    {
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
