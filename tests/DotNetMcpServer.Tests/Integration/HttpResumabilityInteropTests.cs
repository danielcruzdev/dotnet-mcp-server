using System.Net;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drops an SSE connection mid-session and proves the client can pick up exactly where it left
/// off, with nothing lost in between.
/// </summary>
/// <remarks>
/// Resumability belongs to the same escape hatch as sessions themselves: the SDK's event store
/// carries the SEP-2567 obsoletion, and there is no <c>Last-Event-ID</c> to honour without a
/// session to replay it onto. These cases therefore run against <c>--http-sessions</c> on
/// <c>2025-11-25</c> — the last revision that has both sessions and the priming event that
/// starts a resumable stream.
/// <para>
/// <c>2025-06-18</c> would have looked like a working configuration and silently emitted no
/// event ids at all: priming events postdate it. That is why the revision is a named constant
/// rather than whatever the client happens to negotiate.
/// </para>
/// </remarks>
[Trait("Phase", "4")]
public sealed class HttpResumabilityInteropTests : IAsyncLifetime
{
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-resume-" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(DocumentPath("alpha.md"), "# Alpha\n");
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync();
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task The_stream_opens_with_a_priming_event_and_every_event_carries_an_id()
    {
        var server = await StartAsync(withSessions: true);
        using var http = new HttpClient();
        using var deadline = new CancellationTokenSource(StreamTimeout);

        var sessionId = await RawMcpHttp.InitializeAsync(http, server.Endpoint, RawMcpHttp.ResumableProtocol);
        await StartWatchingAsync(http, server.Endpoint, sessionId);

        using var stream = await RawMcpHttp.OpenEventStreamAsync(
            http, server.Endpoint, sessionId, lastEventId: null, RawMcpHttp.ResumableProtocol, deadline.Token);

        var reading = RawMcpHttp.ReadEventsAsync(stream, count: 2, deadline.Token);
        await File.WriteAllTextAsync(DocumentPath("beta.md"), "# Beta\n");
        var events = await reading;

        Assert.Equal(2, events.Count);

        // The priming event is what establishes resumability: it gives the client an id to
        // come back with before the server has anything to say.
        Assert.Equal("prime", events[0].EventType);
        Assert.Equal("message", events[1].EventType);
        Assert.All(events, sseEvent => Assert.False(string.IsNullOrWhiteSpace(sseEvent.Id)));
        Assert.Contains("notifications/resources/list_changed", events[1].Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dropped_stream_resumes_from_last_event_id_without_message_loss()
    {
        var server = await StartAsync(withSessions: true);
        using var http = new HttpClient();
        using var deadline = new CancellationTokenSource(StreamTimeout);

        var sessionId = await RawMcpHttp.InitializeAsync(http, server.Endpoint, RawMcpHttp.ResumableProtocol);
        await StartWatchingAsync(http, server.Endpoint, sessionId);

        string lastEventId;

        using (var first = await RawMcpHttp.OpenEventStreamAsync(
            http, server.Endpoint, sessionId, lastEventId: null, RawMcpHttp.ResumableProtocol, deadline.Token))
        {
            var reading = RawMcpHttp.ReadEventsAsync(first, count: 2, deadline.Token);
            await File.WriteAllTextAsync(DocumentPath("beta.md"), "# Beta\n");
            var received = await reading;

            Assert.Equal(2, received.Count);
            lastEventId = received[^1].Id!;
        }

        // The connection is gone. This change happens while nobody is listening, and is the
        // message the client must not lose.
        await File.WriteAllTextAsync(DocumentPath("gamma.md"), "# Gamma\n");

        using var resumed = await RawMcpHttp.OpenEventStreamAsync(
            http, server.Endpoint, sessionId, lastEventId, RawMcpHttp.ResumableProtocol, deadline.Token);

        var replayed = await RawMcpHttp.ReadEventsAsync(resumed, count: 1, deadline.Token);

        Assert.Single(replayed);
        Assert.Contains("notifications/resources/list_changed", replayed[0].Data, StringComparison.Ordinal);

        // Replay resumes *after* the supplied id rather than repeating it, so the client sees
        // each message once. Without this the test would pass on a server that simply resent
        // everything from the beginning.
        Assert.NotEqual(lastEventId, replayed[0].Id);
    }

    [Fact]
    public async Task The_standalone_stream_is_refused_in_stateless_mode()
    {
        var server = await StartAsync(withSessions: false);
        using var http = new HttpClient();
        using var deadline = new CancellationTokenSource(StreamTimeout);

        await RawMcpHttp.InitializeAsync(http, server.Endpoint, RawMcpHttp.ResumableProtocol);

        using var stream = await RawMcpHttp.OpenEventStreamAsync(
            http, server.Endpoint, sessionId: null, lastEventId: null, RawMcpHttp.ResumableProtocol, deadline.Token);

        // SEP-2567 removed the standalone GET along with sessions, so there is no stream to
        // resume. Pinned so the boundary cannot move quietly.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, stream.StatusCode);
    }

    /// <summary>
    /// Listing resources is what starts the file watcher, and what a real client does before it
    /// could care that the list changed. Without it the first document written below would
    /// raise nothing at all.
    /// </summary>
    private static async Task StartWatchingAsync(HttpClient http, Uri endpoint, string? sessionId)
    {
        using var listed = await RawMcpHttp.PostAsync(
            http,
            endpoint,
            """{"jsonrpc":"2.0","id":2,"method":"resources/list"}""",
            sessionId,
            RawMcpHttp.ResumableProtocol);

        listed.EnsureSuccessStatusCode();
    }

    private async Task<HttpServerProcess> StartAsync(bool withSessions)
    {
        var server = withSessions
            ? await HttpServerProcess.StartAsync(_workspace, "--http-sessions")
            : await HttpServerProcess.StartAsync(_workspace);

        _disposables.Add(server);

        return server;
    }

    private string DocumentPath(string name)
    {
        return Path.Combine(_workspace, name);
    }
}
