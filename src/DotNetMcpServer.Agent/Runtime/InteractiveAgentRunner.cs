using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using DotNetMcpServer.Agent.Mcp;
using DotNetMcpServer.Shared.Json;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Agent.Runtime;

public sealed class InteractiveAgentRunner
{
    private readonly AgentRuntimeSettings _runtimeSettings;
    private readonly OpenAiChatClient _openAiClient;
    private readonly McpClient _mcpClient;

    public InteractiveAgentRunner(AgentRuntimeSettings runtimeSettings, OpenAiChatClient openAiClient, McpClient mcpClient)
    {
        _runtimeSettings = runtimeSettings;
        _openAiClient = openAiClient;
        _mcpClient = mcpClient;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("DotNetMcpServer AI Agent + MCP");
        Console.WriteLine("Digite sua pergunta. Use 'exit' para encerrar.");
        Console.WriteLine();

        var tools = await _mcpClient.ListToolsAsync(cancellationToken);
        Console.WriteLine($"Ferramentas MCP carregadas: {string.Join(", ", tools.Select(tool => tool.Name))}");
        Console.WriteLine();

        // TODO: Implementar context windowing — o histórico cresce indefinidamente e pode exceder o limite de tokens do modelo.
        var messages = new List<JsonObject>
        {
            ChatMessageFactory.System(_runtimeSettings.SystemPrompt)
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("Você > ");
            var userInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            messages.Add(ChatMessageFactory.User(userInput));

            var assistantReply = await CompleteTurnAsync(messages, tools, cancellationToken);
            Console.WriteLine();
            Console.WriteLine($"Agente > {assistantReply}");
            Console.WriteLine();
        }
    }

    private async Task<string> CompleteTurnAsync(List<JsonObject> messages, IReadOnlyList<McpToolDefinition> tools, CancellationToken cancellationToken)
    {
        for (var iteration = 0; iteration < _runtimeSettings.MaxToolIterations; iteration++)
        {
            var assistantTurn = await _openAiClient.CompleteAsync(messages, tools, cancellationToken);

            if (assistantTurn.ToolCalls.Count == 0)
            {
                var content = string.IsNullOrWhiteSpace(assistantTurn.Content)
                    ? "(Sem conteúdo retornado pelo modelo.)"
                    : assistantTurn.Content;

                messages.Add(ChatMessageFactory.Assistant(content));
                return content;
            }

            messages.Add(ChatMessageFactory.AssistantWithToolCalls(assistantTurn));

            foreach (var toolCall in assistantTurn.ToolCalls)
            {
                Console.WriteLine($"[tool] Executando {toolCall.Name}...");
                var result = await _mcpClient.CallToolAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                var toolText = BuildToolContent(result);
                messages.Add(ChatMessageFactory.Tool(toolCall.Id, toolCall.Name, toolText));
            }
        }

        throw new InvalidOperationException("Limite de iterações de tool-calling atingido sem resposta final.");
    }

    private static string BuildToolContent(McpToolCallResult result)
    {
        var content = string.Join(
            Environment.NewLine,
            result.Content.Select(item => item.Text).Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(content))
        {
            content = "(Tool sem retorno textual.)";
        }

        if (result.IsError)
        {
            return $"[TOOL_ERROR]\n{content}";
        }

        return content;
    }
}

internal static class ChatMessageFactory
{
    public static JsonObject System(string content)
    {
        return new JsonObject
        {
            ["role"] = "system",
            ["content"] = content
        };
    }

    public static JsonObject User(string content)
    {
        return new JsonObject
        {
            ["role"] = "user",
            ["content"] = content
        };
    }

    public static JsonObject Assistant(string content)
    {
        return new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = content
        };
    }

    public static JsonObject AssistantWithToolCalls(AssistantTurn turn)
    {
        var toolCalls = new JsonArray();

        foreach (var toolCall in turn.ToolCalls)
        {
            toolCalls.Add(new JsonObject
            {
                ["id"] = toolCall.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = toolCall.Name,
                    ["arguments"] = JsonSerializer.Serialize(toolCall.Arguments, JsonDefaults.SerializerOptions)
                }
            });
        }

        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["tool_calls"] = toolCalls
        };

        if (!string.IsNullOrWhiteSpace(turn.Content))
        {
            message["content"] = turn.Content;
        }

        return message;
    }

    public static JsonObject Tool(string toolCallId, string toolName, string content)
    {
        return new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = toolCallId,
            ["name"] = toolName,
            ["content"] = content
        };
    }
}


