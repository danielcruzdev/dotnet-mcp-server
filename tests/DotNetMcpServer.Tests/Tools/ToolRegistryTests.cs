using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Tests.Tools;

public class ToolRegistryTests
{
    [Fact]
    public void TryGet_RegisteredTool_ReturnsTrue()
    {
        var tool = new FakeTool("test_tool");
        var registry = new ToolRegistry([tool]);

        var found = registry.TryGet("test_tool", out var result);

        Assert.True(found);
        Assert.Same(tool, result);
    }

    [Fact]
    public void TryGet_CaseInsensitive_ReturnsTrue()
    {
        var tool = new FakeTool("test_tool");
        var registry = new ToolRegistry([tool]);

        var found = registry.TryGet("TEST_TOOL", out var result);

        Assert.True(found);
        Assert.Same(tool, result);
    }

    [Fact]
    public void TryGet_UnknownTool_ReturnsFalse()
    {
        var registry = new ToolRegistry([]);

        var found = registry.TryGet("nonexistent", out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void ListDefinitions_ReturnsAllToolsSortedByName()
    {
        var tools = new IMcpTool[]
        {
            new FakeTool("zebra_tool"),
            new FakeTool("alpha_tool"),
            new FakeTool("mid_tool")
        };
        var registry = new ToolRegistry(tools);

        var definitions = registry.ListDefinitions();

        Assert.Equal(3, definitions.Count);
        Assert.Equal("alpha_tool", definitions[0].Name);
        Assert.Equal("mid_tool", definitions[1].Name);
        Assert.Equal("zebra_tool", definitions[2].Name);
    }

    [Fact]
    public void ListDefinitions_EmptyRegistry_ReturnsEmptyList()
    {
        var registry = new ToolRegistry([]);

        var definitions = registry.ListDefinitions();

        Assert.Empty(definitions);
    }

    private sealed class FakeTool : IMcpTool
    {
        public FakeTool(string name)
        {
            Definition = new McpToolDefinition
            {
                Name = name,
                Description = $"Fake tool: {name}",
                InputSchema = new JsonObject { ["type"] = "object" }
            };
        }

        public McpToolDefinition Definition { get; }

        public Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(McpToolCallResult.Success("fake result"));
        }
    }
}
