using ModelContextProtocol.Client;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>Which transport a case is being run over.</summary>
public enum McpTransportKind
{
    /// <summary>Newline-delimited JSON over the subprocess's stdin and stdout.</summary>
    Stdio,

    /// <summary>Streamable HTTP over a real Kestrel listener.</summary>
    Http
}

/// <summary>
/// One running server and one connected official client, over whichever transport a theory
/// asked for.
/// </summary>
/// <remarks>
/// This is what lets a single set of assertions run against both transports. The server is a
/// real subprocess either way and the client is the official SDK's either way; the only
/// difference is how the two are joined, which is the entire point of the exercise.
/// </remarks>
internal sealed class McpServerUnderTest : IAsyncDisposable
{
    private readonly HttpServerProcess? _httpServer;

    private McpServerUnderTest(McpClient client, HttpServerProcess? httpServer)
    {
        Client = client;
        _httpServer = httpServer;
    }

    public McpClient Client { get; }

    public static async Task<McpServerUnderTest> StartAsync(McpTransportKind transport, string workspaceRoot)
    {
        if (transport == McpTransportKind.Stdio)
        {
            var stdio = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "dotnet-mcp-server",
                Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
                Arguments = ["--workspace-root", workspaceRoot]
            }));

            return new McpServerUnderTest(stdio, httpServer: null);
        }

        var server = await HttpServerProcess.StartAsync(workspaceRoot);

        var http = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "dotnet-mcp-server-http",
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        }));

        return new McpServerUnderTest(http, server);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();

        if (_httpServer is not null)
        {
            await _httpServer.DisposeAsync();
        }
    }
}
