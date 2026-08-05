using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives <c>logging/setLevel</c> and the <c>notifications/message</c> stream against the
/// shipped server as a real subprocess.
/// </summary>
/// <remarks>
/// The feature is deprecated by specification revision 2026-07-28 (SEP-2577), which also
/// removed the method in favour of a per-request <c>_meta</c> field. The level-setting cases
/// therefore connect on <c>2025-11-25</c>, the revision every shipping client negotiates;
/// <see cref="Setting_a_level_is_refused_on_the_revision_that_removed_the_method"/> pins the
/// boundary. It is implemented at all because the SDK advertises the logging capability
/// unconditionally, so a server that ignored it would advertise something it never delivers.
/// </remarks>
#pragma warning disable MCP9005
public sealed class LoggingInteropTests : IAsyncLifetime
{
    /// <summary>The last revision on which <c>logging/setLevel</c> exists.</summary>
    private const string SetLevelEraProtocol = "2025-11-25";

    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-logging-" + Guid.NewGuid().ToString("N"));
    private readonly Channel<LoggingMessageNotificationParams> _messages =
        Channel.CreateUnbounded<LoggingMessageNotificationParams>();

    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(Path.Combine(_workspace, "alpha.md"), "# Alpha\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "beta.md"), "# Beta\n");
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

    [Fact]
    public async Task Server_advertises_the_logging_capability()
    {
        var client = await ConnectAsync();

        Assert.NotNull(client.ServerCapabilities.Logging);
    }

    [Fact]
    public async Task Setting_a_level_opens_the_log_stream()
    {
        var client = await ConnectAsync(SetLevelEraProtocol);
        await client.SetLoggingLevelAsync(LoggingLevel.Info);

        await ReadFileAsync(client, "alpha.md");

        var message = await NextMessageAsync();
        Assert.Equal("DotNetMcpServer.Server.Tools.WorkspaceTools", message.Logger);
        Assert.Contains("alpha.md", message.Data.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_logged_before_the_client_asks_for_it()
    {
        var client = await ConnectAsync(SetLevelEraProtocol);

        // Read one file with no level set, then set a level and read another. If the first
        // read had been reported, it would be at the head of the stream and fail this.
        await ReadFileAsync(client, "alpha.md");

        await client.SetLoggingLevelAsync(LoggingLevel.Info);
        await ReadFileAsync(client, "beta.md");

        Assert.Contains("beta.md", (await NextMessageAsync()).Data.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_quieter_level_suppresses_messages_below_it()
    {
        var client = await ConnectAsync(SetLevelEraProtocol);

        await client.SetLoggingLevelAsync(LoggingLevel.Critical);
        await ReadFileAsync(client, "alpha.md");

        await client.SetLoggingLevelAsync(LoggingLevel.Debug);
        await ReadFileAsync(client, "beta.md");

        Assert.Contains("beta.md", (await NextMessageAsync()).Data.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_servers_own_categories_are_the_only_ones_forwarded()
    {
        var client = await ConnectAsync(SetLevelEraProtocol);
        await client.SetLoggingLevelAsync(LoggingLevel.Debug);

        // Debug is verbose enough that the SDK's own categories would flood the stream if they
        // were forwarded — and sending a notification logs a line, which would not terminate.
        await ReadFileAsync(client, "alpha.md");
        await ReadFileAsync(client, "beta.md");

        for (var index = 0; index < 2; index++)
        {
            Assert.StartsWith("DotNetMcpServer.", (await NextMessageAsync()).Logger, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Setting_a_level_is_refused_on_the_revision_that_removed_the_method()
    {
        // 2026-07-28 moved the level onto per-request _meta. Not a defect in this server; the
        // case exists so the boundary is executable rather than folklore.
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAnyAsync<McpException>(
            () => client.SetLoggingLevelAsync(LoggingLevel.Info));

        Assert.Contains("logLevel", exception.Message, StringComparison.Ordinal);
    }

    private async Task<McpClient> ConnectAsync(string? protocolVersion = null)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        var options = new McpClientOptions
        {
            ProtocolVersion = protocolVersion,
            Handlers = new McpClientHandlers
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.LoggingMessageNotification, (notification, cancellationToken) =>
                    {
                        var message = notification.Params?.Deserialize<LoggingMessageNotificationParams>(
                            McpJsonUtilities.DefaultOptions);

                        if (message is not null)
                        {
                            _messages.Writer.TryWrite(message);
                        }

                        return default;
                    })
                ]
            }
        };

        _client = await McpClient.CreateAsync(transport, options);

        return _client;
    }

    private static async Task ReadFileAsync(McpClient client, string path)
    {
        var result = await client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = path });

        Assert.NotEqual(true, result.IsError);
    }

    private async Task<LoggingMessageNotificationParams> NextMessageAsync()
    {
        using var timeout = new CancellationTokenSource(NotificationTimeout);

        return await _messages.Reader.ReadAsync(timeout.Token);
    }
}
#pragma warning restore MCP9005
