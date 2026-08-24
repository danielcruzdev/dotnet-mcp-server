using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives the server as an OAuth 2.1 protected resource: what an anonymous caller is told, and
/// which tokens open the endpoint.
/// </summary>
/// <remarks>
/// The SDK supplies the transport and the metadata document. What is being asserted here is
/// the part that is this server's own responsibility — that a token is actually checked, and
/// that a refusal tells the client where to go and get a good one instead of merely saying no.
/// </remarks>
[Trait("Phase", "4")]
public sealed class HttpAuthorizationInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-auth-" + Guid.NewGuid().ToString("N"));
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
    public async Task An_anonymous_request_is_refused_and_told_where_to_get_a_token()
    {
        var server = await StartProtectedAsync();
        using var http = new HttpClient();

        using var response = await RawMcpHttp.InitializeRawAsync(
            http, server.Endpoint, RawMcpHttp.ResumableProtocol);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // A bare 401 leaves a client with nowhere to go. RFC 9728's whole point is that the
        // challenge names the metadata document, and the document names the authorization
        // server — so discovery needs no out-of-band configuration.
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=", challenge.Parameter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_challenge_points_at_a_metadata_document_that_names_the_authorization_server()
    {
        var server = await StartProtectedAsync(TestTokens.ToolsScope, TestTokens.WriteScope);
        using var http = new HttpClient();

        string metadataUri;
        using (var challenge = await RawMcpHttp.InitializeRawAsync(
            http, server.Endpoint, RawMcpHttp.ResumableProtocol))
        {
            metadataUri = MetadataUriOf(challenge);
        }

        // Followed rather than constructed: the point is that a client can get here knowing
        // only the endpoint it was already talking to.
        using var document = await http.GetAsync(new Uri(metadataUri));
        document.EnsureSuccessStatusCode();

        using var metadata = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        var root = metadata.RootElement;

        Assert.Equal(TestTokens.Resource, root.GetProperty("resource").GetString());
        Assert.Contains(
            root.GetProperty("authorization_servers").EnumerateArray().Select(value => value.GetString()),
            value => value!.StartsWith(TestTokens.Authority, StringComparison.Ordinal));
        Assert.Equal(
            [TestTokens.ToolsScope, TestTokens.WriteScope],
            root.GetProperty("scopes_supported").EnumerateArray().Select(value => value.GetString()));

        // "header" appears once. The SDK pre-populates this, and naming it again in the
        // initializer appended a duplicate rather than replacing it.
        Assert.Equal(
            ["header"],
            root.GetProperty("bearer_methods_supported").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task A_valid_token_opens_the_endpoint()
    {
        var server = await StartProtectedAsync();
        using var http = Authenticated(TestTokens.Issue());

        using var response = await RawMcpHttp.InitializeRawAsync(
            http, server.Endpoint, RawMcpHttp.ResumableProtocol);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_official_client_works_against_the_protected_endpoint()
    {
        var server = await StartProtectedAsync();

        var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "dotnet-mcp-server-http",
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Scoped, because the tools declare the scopes they need and an unscoped
                // token would legitimately be offered none of them.
                ["Authorization"] = "Bearer " + TestTokens.Issue(scopes: [TestTokens.ToolsScope])
            }
        }));

        _disposables.Add(client);

        var tools = await client.ListToolsAsync();

        Assert.NotEmpty(tools);
    }

    [Theory]
    // Minted for a different MCP server. Without an audience check this would open every
    // resource that trusts the same issuer, which is the failure RFC 8707 exists to prevent.
    [InlineData("wrong-audience")]
    // Correct shape, correct signature, issued by someone else.
    [InlineData("wrong-issuer")]
    // Was valid. Is not now.
    [InlineData("expired")]
    // Signed with a key the server does not trust.
    [InlineData("forged")]
    public async Task A_token_that_does_not_check_out_is_refused(string flaw)
    {
        var server = await StartProtectedAsync();
        using var http = Authenticated(TokenWith(flaw));

        using var response = await RawMcpHttp.InitializeRawAsync(
            http, server.Endpoint, RawMcpHttp.ResumableProtocol);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unprotected_server_serves_without_a_token()
    {
        // Naming no authority leaves the endpoint open, which is what a stdio launch and a
        // loopback development run need. Pinned so that default cannot change unnoticed.
        var server = await StartAsync();
        using var http = new HttpClient();

        using var response = await RawMcpHttp.InitializeRawAsync(
            http, server.Endpoint, RawMcpHttp.ResumableProtocol);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string TokenWith(string flaw)
    {
        return flaw switch
        {
            "wrong-audience" => TestTokens.Issue(audience: "https://mcp.example.test/some-other-server"),
            "wrong-issuer" => TestTokens.Issue(issuer: "https://attacker.example.test"),
            "expired" => TestTokens.Expired(),
            "forged" => ForgedToken(),
            _ => throw new ArgumentOutOfRangeException(nameof(flaw), flaw, "Unknown flaw.")
        };
    }

    /// <summary>A well-formed token signed with a key this server never trusted.</summary>
    private static string ForgedToken()
    {
        var genuine = TestTokens.Issue();
        var parts = genuine.Split('.');

        // Same header and payload, a signature that is simply wrong.
        return string.Join('.', parts[0], parts[1], "Zm9yZ2VkLXNpZ25hdHVyZQ");
    }

    private static HttpClient Authenticated(string token)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return http;
    }

    private static string MetadataUriOf(HttpResponseMessage response)
    {
        var parameter = Assert.Single(response.Headers.WwwAuthenticate).Parameter!;
        var marker = "resource_metadata=";
        var start = parameter.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var value = parameter[start..].TrimStart('"');
        var end = value.IndexOf('"', StringComparison.Ordinal);

        return end >= 0 ? value[..end] : value;
    }

    private Task<HttpServerProcess> StartProtectedAsync(params string[] scopes)
    {
        return StartAsync(TestTokens.ServerArguments(scopes));
    }

    private async Task<HttpServerProcess> StartAsync(params string[] extraArguments)
    {
        var server = await HttpServerProcess.StartAsync(_workspace, extraArguments);

        _disposables.Add(server);

        return server;
    }
}
