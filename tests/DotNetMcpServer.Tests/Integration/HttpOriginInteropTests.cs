using System.Net;
using DotNetMcpServer.Server.Hosting;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the <c>Origin</c> check the MCP specification requires of a Streamable HTTP server,
/// against the real listener.
/// </summary>
/// <remarks>
/// The threat is DNS rebinding: a page the user already has open is made to resolve its
/// hostname to <c>127.0.0.1</c> and then talks to this server with the user's own machine
/// authority. The one thing that page cannot forge is its <c>Origin</c>, which is why these
/// cases are about a header rather than about an address.
/// </remarks>
[Trait("Phase", "4")]
public sealed class HttpOriginInteropTests : IAsyncLifetime
{
    private const string AllowedOrigin = "https://app.example.com";

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-origin-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    // Not a browser, no header — every client this server is actually built for.
    [InlineData(null, HttpStatusCode.OK)]
    // A page served from the developer's own machine. Rebinding cannot produce this origin:
    // the attacking page keeps its own, which is the attacker's domain.
    [InlineData("http://localhost:3000", HttpStatusCode.OK)]
    [InlineData("http://127.0.0.1:8080", HttpStatusCode.OK)]
    // The rebinding attempt itself.
    [InlineData("https://evil.example", HttpStatusCode.Forbidden)]
    // A sandboxed iframe. A real origin value, and not one an operator can have meant to allow.
    [InlineData("null", HttpStatusCode.Forbidden)]
    public async Task An_origin_is_served_only_when_it_is_loopback_or_configured(
        string? origin,
        HttpStatusCode expected)
    {
        var server = await StartAsync();

        Assert.Equal(expected, await InitializeWithOriginAsync(server, origin));
    }

    [Fact]
    public async Task A_configured_origin_is_served()
    {
        var server = await StartAsync("--allowed-origin", AllowedOrigin);

        Assert.Equal(HttpStatusCode.OK, await InitializeWithOriginAsync(server, AllowedOrigin));
    }

    [Fact]
    public async Task Configuring_one_origin_does_not_admit_the_others()
    {
        var server = await StartAsync("--allowed-origin", AllowedOrigin);

        Assert.Equal(HttpStatusCode.Forbidden, await InitializeWithOriginAsync(server, "https://evil.example"));
    }

    [Fact]
    public void The_default_binding_is_loopback()
    {
        // Asserted on the default rather than by starting a server without --urls, which would
        // have to take the fixed default port and would fail for whoever already had it. The
        // property that matters is that the default is not reachable from the network, and
        // that is a property of the value.
        Assert.True(
            Uri.TryCreate(HttpServerHost.DefaultUrls, UriKind.Absolute, out var uri) && uri.IsLoopback,
            $"The default binding '{HttpServerHost.DefaultUrls}' is reachable from outside this machine.");
    }

    private static async Task<HttpStatusCode> InitializeWithOriginAsync(HttpServerProcess server, string? origin)
    {
        using var http = new HttpClient();

        if (origin is not null)
        {
            http.DefaultRequestHeaders.Add("Origin", origin);
        }

        using var response = await RawMcpHttp.InitializeRawAsync(
            http,
            server.Endpoint,
            RawMcpHttp.ResumableProtocol);

        return response.StatusCode;
    }

    private async Task<HttpServerProcess> StartAsync(params string[] extraArguments)
    {
        var server = await HttpServerProcess.StartAsync(_workspace, extraArguments);

        _disposables.Add(server);

        return server;
    }
}
