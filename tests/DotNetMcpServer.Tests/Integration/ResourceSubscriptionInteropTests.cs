using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives resource subscriptions against the shipped server as a real subprocess, and waits
/// for the notifications a real client would wait for.
/// </summary>
/// <remarks>
/// Two protocol revisions are exercised deliberately. <c>resources/subscribe</c> was removed
/// by <c>2026-07-28</c> (SEP-2575) in favour of <c>subscriptions/listen</c>, so the
/// per-resource cases connect on <c>2025-11-25</c> — the revision every shipping client still
/// negotiates — while the list-changed cases run on whatever the SDK defaults to, which is the
/// newer one. <see cref="Subscribing_is_refused_on_the_revision_that_removed_the_method"/>
/// pins that boundary so it cannot move silently.
/// <para>
/// The client registers raw notification handlers rather than the per-subscription callback:
/// most of these cases are about what the server chose <em>not</em> to send, which a filtered
/// callback would hide.
/// </para>
/// </remarks>
public sealed class ResourceSubscriptionInteropTests : IAsyncLifetime
{
    /// <summary>The last revision on which <c>resources/subscribe</c> exists.</summary>
    private const string SubscribeEraProtocol = "2025-11-25";

    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-subs-" + Guid.NewGuid().ToString("N"));
    private readonly Channel<string> _updates = Channel.CreateUnbounded<string>();
    private readonly Channel<bool> _listChanges = Channel.CreateUnbounded<bool>();

    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);
        await File.WriteAllTextAsync(AbsolutePath("alpha.md"), "# Alpha\n");
        await File.WriteAllTextAsync(AbsolutePath("beta.md"), "# Beta\n");
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
    public async Task Server_advertises_subscriptions_and_a_watched_list()
    {
        var client = await ConnectAsync();

        var resources = client.ServerCapabilities.Resources;

        Assert.NotNull(resources);
        Assert.True(resources.Subscribe);
        Assert.True(resources.ListChanged);
    }

    [Fact]
    public async Task Editing_a_subscribed_document_notifies_the_client()
    {
        var client = await ConnectAsync(SubscribeEraProtocol);
        await client.SubscribeToResourceAsync("workspace://file/alpha.md");

        await File.WriteAllTextAsync(AbsolutePath("alpha.md"), "# Alpha\nEdited.\n");

        Assert.Equal("workspace://file/alpha.md", await NextUpdateAsync());
    }

    [Fact]
    public async Task Editing_a_document_nobody_subscribed_to_notifies_nothing()
    {
        var client = await ConnectAsync(SubscribeEraProtocol);
        await client.SubscribeToResourceAsync("workspace://file/alpha.md");

        // beta first: were the server notifying indiscriminately, beta's update would arrive
        // before alpha's and fail the assertion.
        await File.WriteAllTextAsync(AbsolutePath("beta.md"), "# Beta\nEdited.\n");
        await File.WriteAllTextAsync(AbsolutePath("alpha.md"), "# Alpha\nEdited.\n");

        Assert.Equal("workspace://file/alpha.md", await NextUpdateAsync());
    }

    [Fact]
    public async Task Unsubscribing_stops_the_updates()
    {
        var client = await ConnectAsync(SubscribeEraProtocol);
        await client.SubscribeToResourceAsync("workspace://file/alpha.md");
        await client.SubscribeToResourceAsync("workspace://file/beta.md");
        await client.UnsubscribeFromResourceAsync("workspace://file/alpha.md");

        await File.WriteAllTextAsync(AbsolutePath("alpha.md"), "# Alpha\nEdited.\n");
        await File.WriteAllTextAsync(AbsolutePath("beta.md"), "# Beta\nEdited.\n");

        Assert.Equal("workspace://file/beta.md", await NextUpdateAsync());
    }

    [Fact]
    public async Task Subscribing_to_a_uri_this_server_cannot_serve_is_rejected()
    {
        var client = await ConnectAsync(SubscribeEraProtocol);

        await Assert.ThrowsAnyAsync<McpException>(
            () => client.SubscribeToResourceAsync("https://example.com/alpha.md"));
    }

    [Fact]
    public async Task Subscribing_is_refused_on_the_revision_that_removed_the_method()
    {
        // Not a defect in this server: 2026-07-28 deleted resources/subscribe, and the SDK
        // enforces that. The case exists so the boundary is a documented, executable fact
        // rather than a surprise the next time a client upgrades.
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAnyAsync<McpException>(
            () => client.SubscribeToResourceAsync("workspace://file/alpha.md"));

        Assert.Contains("subscriptions/listen", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_a_document_raises_list_changed()
    {
        // Listing is what starts the watcher, and it is what a client does before it could
        // care that the list changed. This one runs on the current revision.
        var client = await ConnectAsync();
        await client.ListResourcesAsync();

        await File.WriteAllTextAsync(AbsolutePath("gamma.md"), "# Gamma\n");

        Assert.True(await ReadAsync(_listChanges.Reader));
    }

    [Fact]
    public async Task Changes_to_ignored_files_raise_nothing()
    {
        var client = await ConnectAsync();
        await client.ListResourcesAsync();

        Directory.CreateDirectory(AbsolutePath("obj"));
        await File.WriteAllTextAsync(AbsolutePath(Path.Combine("obj", "generated.md")), "build output");
        await File.WriteAllTextAsync(AbsolutePath("picture.png"), "not a document");

        // Only the document that follows is expected to reach the client.
        await File.WriteAllTextAsync(AbsolutePath("gamma.md"), "# Gamma\n");

        Assert.True(await ReadAsync(_listChanges.Reader));
        Assert.False(_listChanges.Reader.TryRead(out _));
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
                    new(NotificationMethods.ResourceUpdatedNotification, (notification, cancellationToken) =>
                    {
                        var uri = notification.Params?.Deserialize<ResourceUpdatedNotificationParams>(
                            McpJsonUtilities.DefaultOptions)?.Uri;

                        if (uri is not null)
                        {
                            _updates.Writer.TryWrite(uri);
                        }

                        return default;
                    }),
                    new(NotificationMethods.ResourceListChangedNotification, (notification, cancellationToken) =>
                    {
                        _listChanges.Writer.TryWrite(true);

                        return default;
                    })
                ]
            }
        };

        _client = await McpClient.CreateAsync(transport, options);

        return _client;
    }

    private string AbsolutePath(string relativePath)
    {
        return Path.Combine(_workspace, relativePath);
    }

    private async Task<string> NextUpdateAsync()
    {
        return await ReadAsync(_updates.Reader);
    }

    private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
    {
        using var timeout = new CancellationTokenSource(NotificationTimeout);

        return await reader.ReadAsync(timeout.Token);
    }
}
