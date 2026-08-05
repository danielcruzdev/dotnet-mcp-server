using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives a full elicitation round trip against the shipped server as a real subprocess: the
/// tool is called without a title, the server asks the client for one, and the answer lands in
/// the note on disk.
/// </summary>
public sealed class ElicitationInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-elicit-" + Guid.NewGuid().ToString("N"));

    private McpClient? _client;
    private ElicitRequestParams? _lastRequest;

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
    public async Task A_tool_asks_the_user_for_the_argument_it_was_not_given()
    {
        var client = await ConnectAsync(Answer("Newline framing"));

        var result = await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Messages are delimited by newlines." });

        Assert.NotEqual(true, result.IsError);

        // The server asked, and it asked for something a person could answer.
        Assert.NotNull(_lastRequest);
        Assert.Contains("called", _lastRequest.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(_lastRequest.RequestedSchema);
        Assert.True(_lastRequest.RequestedSchema.Properties.ContainsKey("title"));

        Assert.Contains("## Newline framing", await ReadNotesAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_title_supplied_by_the_caller_is_not_asked_about()
    {
        var client = await ConnectAsync(Answer("Should not be used"));

        await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Body.", ["title"] = "Given" });

        Assert.Null(_lastRequest);
        Assert.Contains("## Given", await ReadNotesAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declining_still_saves_the_note()
    {
        // Refusing to name a note is an answer, not a failure. Losing the note over it would
        // be the wrong trade.
        var client = await ConnectAsync((request, cancellationToken) =>
        {
            _lastRequest = request;

            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
        });

        var result = await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "Saved anyway." });

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(_lastRequest);

        var notes = await ReadNotesAsync();
        Assert.Contains("## Note", notes, StringComparison.Ordinal);
        Assert.Contains("Saved anyway.", notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_that_cannot_be_asked_is_not_asked()
    {
        // No elicitation handler means no elicitation capability in initialize. The tool has
        // to notice and fall back rather than fail.
        var client = await ConnectAsync(elicitationHandler: null);

        var result = await client.CallToolAsync(
            "append_study_note",
            new Dictionary<string, object?> { ["note"] = "No one to ask." });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("## Note", await ReadNotesAsync(), StringComparison.Ordinal);
    }

    private Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> Answer(string title)
    {
        return (request, cancellationToken) =>
        {
            _lastRequest = request;

            return ValueTask.FromResult(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["title"] = JsonSerializer.SerializeToElement(title)
                }
            });
        };
    }

    private async Task<McpClient> ConnectAsync(
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        var options = new McpClientOptions
        {
            Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler }
        };

        _client = await McpClient.CreateAsync(transport, options);

        return _client;
    }

    private async Task<string> ReadNotesAsync()
    {
        return await File.ReadAllTextAsync(Path.Combine(_workspace, "notes", "study-notes.md"));
    }
}
