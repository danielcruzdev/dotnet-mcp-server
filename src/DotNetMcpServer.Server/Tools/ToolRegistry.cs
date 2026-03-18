using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _tools;

    public ToolRegistry(IEnumerable<IMcpTool> tools)
    {
        _tools = tools.ToDictionary(tool => tool.Definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    public List<McpToolDefinition> ListDefinitions()
    {
        return _tools.Values
            .Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryGet(string toolName, out IMcpTool? tool)
    {
        return _tools.TryGetValue(toolName, out tool);
    }
}

