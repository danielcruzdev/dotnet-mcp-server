using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Exercises the two Streamable HTTP modes this server can run in, and pins the boundary
/// between them.
/// </summary>
/// <remarks>
/// SEP-2567 removed <c>Mcp-Session-Id</c> from Streamable HTTP in the <c>2026-07-28</c>
/// revision, so stateless is what the current specification describes and what this server
/// serves by default. Stateful mode remains available behind <c>--http-sessions</c> because
/// the things it can do — unsolicited notifications, and therefore sampling and elicitation —
/// stop working without a session, and those are capabilities Phase 3 built.
/// <para>
/// The pair <see cref="Session_mode_pushes_list_changed_to_the_client"/> and
/// <see cref="Stateless_mode_pushes_nothing_to_the_client"/> is the point of this class: it
/// makes the cost of the specification's own default an executable fact rather than a claim in
/// a comment.
/// </para>
/// </remarks>
[Trait("Phase", "4")]
public sealed class HttpSessionInteropTests : IAsyncLifetime
{
    private const string SessionIdHeader = "Mcp-Session-Id";

    /// <summary>A revision on which sessions still exist, for the raw-HTTP cases.</summary>
    private const string SessionEraProtocol = "2025-06-18";

    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Written as a constant rather than interpolated: the nested braces of a JSON-RPC
    /// envelope fight every form of raw string literal the compiler offers.
    /// </summary>
    private const string InitializePayload =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"interop-test","version":"1.0"}}}""";

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-sess-" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _disposables = [];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(Path.Combine(_workspace, "alpha.md"), "# Alpha\n");
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
    public async Task Stateless_mode_issues_no_session_id()
    {
        var server = await StartAsync();
        using var http = new HttpClient();

        using var response = await InitializeAsync(http, server.Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(SessionIdHeader));
    }

    [Fact]
    public async Task Session_mode_issues_a_session_id()
    {
        var server = await StartAsync(withSessions: true);
        using var http = new HttpClient();

        using var response = await InitializeAsync(http, server.Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(SessionIdOf(response)));
    }

    [Fact]
    public async Task Session_mode_serves_later_requests_on_the_same_session()
    {
        var server = await StartAsync(withSessions: true);
        using var http = new HttpClient();

        string sessionId;
        using (var initialize = await InitializeAsync(http, server.Endpoint))
        {
            sessionId = SessionIdOf(initialize)!;
        }

        using var listed = await PostAsync(
            http,
            server.Endpoint,
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            sessionId);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Contains("calculate_expression", await listed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_session_ends_it()
    {
        var server = await StartAsync(withSessions: true);
        using var http = new HttpClient();

        string sessionId;
        using (var initialize = await InitializeAsync(http, server.Endpoint))
        {
            sessionId = SessionIdOf(initialize)!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, server.Endpoint);
        request.Headers.Add(SessionIdHeader, sessionId);
        request.Headers.Add("MCP-Protocol-Version", SessionEraProtocol);
        using var deleted = await http.SendAsync(request);

        Assert.True(
            deleted.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"DELETE returned {(int)deleted.StatusCode}.");

        using var afterDelete = await PostAsync(
            http,
            server.Endpoint,
            """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""",
            sessionId);

        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task An_unknown_session_id_is_refused()
    {
        var server = await StartAsync(withSessions: true);
        using var http = new HttpClient();

        // The session must exist before an unknown id means anything: a server with no
        // sessions at all could plausibly answer 404 for a different reason.
        using (await InitializeAsync(http, server.Endpoint))
        {
        }

        using var response = await PostAsync(
            http,
            server.Endpoint,
            """{"jsonrpc":"2.0","id":4,"method":"tools/list"}""",
            sessionId: "a-session-that-was-never-issued");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Session_mode_pushes_list_changed_to_the_client()
    {
        var listChanges = Channel.CreateUnbounded<bool>();
        var server = await StartAsync(withSessions: true);
        var client = await ConnectAsync(server, listChanges);

        // Listing is what starts the watcher, and what a client does before it could care that
        // the list changed.
        await client.ListResourcesAsync();

        await File.WriteAllTextAsync(Path.Combine(_workspace, "beta.md"), "# Beta\n");

        Assert.True(await ReadWithinTimeoutAsync(listChanges.Reader));
    }

    [Fact]
    public async Task Stateless_mode_pushes_nothing_to_the_client()
    {
        var listChanges = Channel.CreateUnbounded<bool>();
        var server = await StartAsync();
        var client = await ConnectAsync(server, listChanges);

        await client.ListResourcesAsync();

        await File.WriteAllTextAsync(Path.Combine(_workspace, "beta.md"), "# Beta\n");

        // Not a weaker assertion than the stateful case — a deliberate one. Stateless mode has
        // no channel on which an unsolicited message could reach the client, so the watcher
        // fires and the notification goes nowhere. The wait matches the stateful test's, so
        // this fails if delivery ever starts working rather than passing by being impatient.
        using var timeout = new CancellationTokenSource(NotificationTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await listChanges.Reader.ReadAsync(timeout.Token));
    }

    private async Task<HttpServerProcess> StartAsync(bool withSessions = false)
    {
        var server = withSessions
            ? await HttpServerProcess.StartAsync(_workspace, "--http-sessions")
            : await HttpServerProcess.StartAsync(_workspace);

        _disposables.Add(server);

        return server;
    }

    private async Task<McpClient> ConnectAsync(HttpServerProcess server, Channel<bool> listChanges)
    {
        var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "dotnet-mcp-server-http",
                Endpoint = server.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,

                // The standalone GET is the stream an unsolicited notification arrives on.
                // Without it the stateful case would fail for a client-side reason.
                EnableStandaloneGetStream = true
            }),
            new McpClientOptions
            {
                Handlers = new McpClientHandlers
                {
                    NotificationHandlers =
                    [
                        new(NotificationMethods.ResourceListChangedNotification, (notification, cancellationToken) =>
                        {
                            listChanges.Writer.TryWrite(true);

                            return default;
                        })
                    ]
                }
            });

        _disposables.Add(client);

        return client;
    }

    private static async Task<HttpResponseMessage> InitializeAsync(HttpClient http, Uri endpoint)
    {
        var response = await PostAsync(
            http,
            endpoint,
            InitializePayload,
            sessionId: null);

        var sessionId = SessionIdOf(response);

        if (sessionId is not null)
        {
            using var initialized = await PostAsync(
                http,
                endpoint,
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                sessionId);
        }

        return response;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient http,
        Uri endpoint,
        string payload,
        string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", SessionEraProtocol);

        if (sessionId is not null)
        {
            request.Headers.Add(SessionIdHeader, sessionId);
        }

        return await http.SendAsync(request);
    }

    private static string? SessionIdOf(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(SessionIdHeader, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static async Task<bool> ReadWithinTimeoutAsync(ChannelReader<bool> reader)
    {
        using var timeout = new CancellationTokenSource(NotificationTimeout);

        return await reader.ReadAsync(timeout.Token);
    }
}
