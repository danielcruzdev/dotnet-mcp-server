using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Drives <c>completion/complete</c> against the shipped server as a real subprocess.
/// </summary>
public sealed class CompletionInteropTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "mcp-complete-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "docs"));

        await File.WriteAllTextAsync(Path.Combine(_workspace, "docs", "design.md"), "# Design\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "docs", "install.md"), "# Install\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "readme.md"), "# Readme\n");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            Arguments = ["--workspace-root", _workspace]
        });

        _client = await McpClient.CreateAsync(transport);
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
    public void Server_advertises_the_completions_capability()
    {
        Assert.NotNull(Client.ServerCapabilities.Completions);
    }

    [Fact]
    public async Task Completing_a_prompt_path_narrows_to_matching_documents()
    {
        var result = await Client.CompleteAsync(
            new PromptReference { Name = "summarize_document" },
            "path",
            "docs/");

        Assert.Equal(["docs/design.md", "docs/install.md"], result.Completion.Values.Order(StringComparer.Ordinal));
        Assert.Equal(2, result.Completion.Total);
        Assert.NotEqual(true, result.Completion.HasMore);
    }

    [Fact]
    public async Task Completing_an_empty_path_offers_every_document()
    {
        var result = await Client.CompleteAsync(
            new PromptReference { Name = "summarize_document" },
            "path",
            string.Empty);

        Assert.Equal(3, result.Completion.Values.Count);
    }

    [Fact]
    public async Task Completing_a_resource_template_path_uses_the_same_documents()
    {
        var result = await Client.CompleteAsync(
            new ResourceTemplateReference { Uri = "workspace://excerpt/{start}-{end}/{+path}" },
            "path",
            "read");

        Assert.Equal(["readme.md"], result.Completion.Values);
    }

    [Fact]
    public async Task Completing_a_fixed_argument_comes_from_its_allowed_values()
    {
        // audience is [AllowedValues(...)]-decorated, which the SDK completes on the server's
        // behalf. The custom handler must not have displaced that.
        var result = await Client.CompleteAsync(
            new PromptReference { Name = "summarize_document" },
            "audience",
            "re");

        Assert.Equal(["reviewer"], result.Completion.Values);
    }

    [Fact]
    public async Task Completing_an_argument_the_server_cannot_enumerate_returns_nothing()
    {
        var result = await Client.CompleteAsync(
            new PromptReference { Name = "study_plan" },
            "topic",
            "res");

        Assert.Empty(result.Completion.Values);
    }
}
