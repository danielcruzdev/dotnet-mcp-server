using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives a long-running tool with a progress token and asserts the client sees the work
/// advance, against the shipped server as a real subprocess.
/// </summary>
/// <remarks>
/// <para>
/// Reports are collected through a notification handler bound to
/// <c>notifications/progress</c>, not through the <see cref="IProgress{T}"/> overload of
/// <see cref="McpClient.CallToolAsync"/>. The SDK ties that overload's registration to the
/// lifetime of the request: when the response arrives the token is forgotten, and a report the
/// client has read but not yet dispatched is dropped on the floor. Under a full-suite run that
/// discarded every report rather than the last one — the failure read <c>reports=0</c>.
/// </para>
/// <para>
/// A handler bound to the method outlives the request, so a report that is merely late still
/// arrives. That turns "how long should we wait" from a guess into a bound.
/// </para>
/// </remarks>
public sealed class ProgressInteropTests : IAsyncLifetime
{
    private const int DocumentCount = 12;

    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-progress-" + Guid.NewGuid().ToString("N"));
    private readonly Channel<ProgressNotificationParams> _reports =
        Channel.CreateUnbounded<ProgressNotificationParams>();

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

        var options = new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.ProgressNotification, (notification, cancellationToken) =>
                    {
                        var report = notification.Params?.Deserialize<ProgressNotificationParams>(
                            McpJsonUtilities.DefaultOptions);

                        if (report is not null)
                        {
                            _reports.Writer.TryWrite(report);
                        }

                        return default;
                    })
                ]
            }
        };

        _client = await McpClient.CreateAsync(transport, options);
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
        var token = new ProgressToken("scan-" + Guid.NewGuid().ToString("N"));

        var result = await Client.CallToolAsync(
            "scan_workspace",
            options: new RequestOptions { ProgressToken = token });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains($"\"documents\":{DocumentCount}", TextOf(result), StringComparison.Ordinal);

        // Every document the tool walked owes exactly one report, including the last: the
        // response no longer cancels their delivery. Each is awaited rather than counted after
        // a sleep, so a slow machine makes the test slower and never makes it wrong.
        var reports = new List<ProgressNotificationValue>();

        for (var index = 0; index < DocumentCount; index++)
        {
            var report = await NextReportAsync();

            Assert.Equal(token, report.ProgressToken);
            reports.Add(report.Progress);
        }

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

        // Nothing asked for progress, so nothing may be sent. The tool has already returned,
        // and the reports would have been written to the pipe ahead of the response it read.
        Assert.False(_reports.Reader.TryRead(out _));
    }

    private async Task<ProgressNotificationParams> NextReportAsync()
    {
        using var timeout = new CancellationTokenSource(ReportTimeout);

        return await _reports.Reader.ReadAsync(timeout.Token);
    }

    private static string TextOf(CallToolResult result)
    {
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
