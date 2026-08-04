using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives a sampling round trip against the shipped server as a real subprocess: the server
/// borrows the client's model to name a note, and the name reaches the file on disk.
/// </summary>
/// <remarks>
/// Sampling is deprecated by specification revision 2026-07-28 (SEP-2577) and the SDK marks
/// the API obsolete, which is why the suppression is here as well as in the server. The
/// feature still works on every revision a client currently negotiates.
/// </remarks>
#pragma warning disable MCP9005
public sealed class SamplingInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-sampling-" + Guid.NewGuid().ToString("N"));

    private McpClient? _client;
    private CreateMessageRequestParams? _lastRequest;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_workspace);

        return Task.CompletedTask;
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
    public async Task The_server_borrows_the_clients_model_to_name_a_note()
    {
        var client = await ConnectAsync(Replying("Newline Framing"));

        var result = await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Messages are delimited by newlines." });

        Assert.NotEqual(true, result.IsError);

        // The server asked, and it asked for something small and specific.
        Assert.NotNull(_lastRequest);
        Assert.Equal(32, _lastRequest.MaxTokens);
        Assert.Contains("title", _lastRequest.SystemPrompt, StringComparison.OrdinalIgnoreCase);

        var prompt = Assert.IsType<TextContentBlock>(Assert.Single(Assert.Single(_lastRequest.Messages).Content));
        Assert.Equal("Messages are delimited by newlines.", prompt.Text);

        Assert.Contains("## Newline Framing", await ReadNotesAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_title_supplied_by_the_caller_needs_no_model()
    {
        var client = await ConnectAsync(Replying("Should not be used"));

        await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Body.", ["title"] = "Given" });

        Assert.Null(_lastRequest);
        Assert.Contains("## Given", await ReadNotesAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_that_lends_no_model_still_saves_the_note()
    {
        var client = await ConnectAsync(samplingHandler: null);

        var result = await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Nobody to ask." });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("## Note", await ReadNotesAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_answers_with_a_paragraph_does_not_corrupt_the_notes_file()
    {
        // Generated content going into a structured document. A two-line answer written
        // straight into "## {title}" would break the markdown for every note after it.
        var client = await ConnectAsync(Replying("\"The Title\"\nAnd then some commentary the model added."));

        await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Body." });

        var notes = await ReadNotesAsync();
        Assert.Contains("## The Title", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("commentary", notes, StringComparison.Ordinal);
    }

    private Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>> Replying(string title)
    {
        return (request, progress, cancellationToken) =>
        {
            _lastRequest = request;

            return ValueTask.FromResult(new CreateMessageResult
            {
                Model = "stub",
                Role = Role.Assistant,
                Content = [new TextContentBlock { Text = title }]
            });
        };
    }

    private async Task<McpClient> ConnectAsync(
        Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>>? samplingHandler)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        var options = new McpClientOptions
        {
            Handlers = new McpClientHandlers { SamplingHandler = samplingHandler }
        };

        _client = await McpClient.CreateAsync(transport, options);

        return _client;
    }

    private async Task<string> ReadNotesAsync()
    {
        return await File.ReadAllTextAsync(Path.Combine(_workspace, "notes", "study-notes.md"));
    }
}
#pragma warning restore MCP9005
