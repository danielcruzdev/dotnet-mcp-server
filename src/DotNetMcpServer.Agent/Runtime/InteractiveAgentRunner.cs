using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using DotNetMcpServer.Agent.Json;

namespace DotNetMcpServer.Agent.Runtime;

public sealed partial class InteractiveAgentRunner
{
    private readonly AgentRuntimeSettings _runtimeSettings;
    private readonly OpenAiChatClient _openAiClient;
    private readonly IUserInput _userInput;
    private readonly ILogger<InteractiveAgentRunner> _logger;

    public InteractiveAgentRunner(
        IOptions<AgentRuntimeSettings> runtimeSettings,
        OpenAiChatClient openAiClient,
        IUserInput userInput,
        ILogger<InteractiveAgentRunner> logger)
    {
        _runtimeSettings = runtimeSettings.Value;
        _openAiClient = openAiClient;
        _userInput = userInput;
        _logger = logger;
    }

    /// <remarks>
    /// <see cref="Console"/> here is the conversation with the user, not logging. Diagnostics
    /// go through <see cref="ILogger"/>. The agent may use stdout freely — the stdout
    /// restriction belongs to the MCP server, where stdout is the protocol channel.
    /// </remarks>
    public async Task RunAsync(McpClient mcpClient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mcpClient);

        Console.WriteLine("DotNetMcpServer AI Agent + MCP");
        Console.WriteLine("Type your question. Use 'exit' to quit.");
        Console.WriteLine();

        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"MCP tools loaded: {string.Join(", ", tools.Select(tool => tool.Name))}");
        Console.WriteLine();

        // TODO: Implement context windowing — history grows without bound and will exceed the
        // model's token limit in a long session. Tracked as F6-04.
        var messages = new List<JsonObject>
        {
            ChatMessageFactory.System(_runtimeSettings.SystemPrompt)
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("You > ");
            var userInput = await _userInput.ReadLineAsync(cancellationToken);

            // null is end of input, not an empty line. Treating the two alike made a closed
            // stdin spin the loop at full CPU, reprinting the prompt forever.
            if (userInput is null || userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            messages.Add(ChatMessageFactory.User(userInput));

            var assistantReply = await CompleteTurnAsync(mcpClient, messages, tools, cancellationToken);
            Console.WriteLine();
            Console.WriteLine($"Agent > {assistantReply}");
            Console.WriteLine();
        }
    }

    internal async Task<string> CompleteTurnAsync(McpClient mcpClient, List<JsonObject> messages, IList<McpClientTool> tools, CancellationToken cancellationToken)
    {
        // Whatever the model says while it is still calling tools. If it never reaches a final
        // answer, this narration is the partial answer the user gets.
        var narration = new List<string>();

        for (var iteration = 0; iteration < _runtimeSettings.MaxToolIterations; iteration++)
        {
            var assistantTurn = await _openAiClient.CompleteAsync(messages, tools, cancellationToken);

            if (assistantTurn.ToolCalls.Count == 0)
            {
                var content = string.IsNullOrWhiteSpace(assistantTurn.Content)
                    ? "(The model returned no content.)"
                    : assistantTurn.Content;

                messages.Add(ChatMessageFactory.Assistant(content));
                return content;
            }

            if (!string.IsNullOrWhiteSpace(assistantTurn.Content))
            {
                narration.Add(assistantTurn.Content.Trim());
            }

            messages.Add(ChatMessageFactory.AssistantWithToolCalls(assistantTurn));

            foreach (var toolCall in assistantTurn.ToolCalls)
            {
                LogExecutingTool(toolCall.Name);
                var arguments = toolCall.Arguments.ToDictionary(
                    argument => argument.Key,
                    argument => (object?)argument.Value);
                var result = await mcpClient.CallToolAsync(toolCall.Name, arguments, cancellationToken: cancellationToken);
                var toolText = BuildToolContent(result);
                messages.Add(ChatMessageFactory.Tool(toolCall.Id, toolCall.Name, toolText));
            }
        }

        LogIterationLimitReached(_runtimeSettings.MaxToolIterations);

        // The limit is a budget, not a failure. Throwing here ended the whole session over one
        // stubborn turn — the user lost the conversation because the model would not stop
        // calling tools. The history stays intact, so the next turn can carry on.
        var partial = FormatExhaustedTurn(narration, _runtimeSettings.MaxToolIterations);
        messages.Add(ChatMessageFactory.Assistant(partial));

        return partial;
    }

    /// <summary>
    /// Renders the answer given when the tool budget runs out. Separated from the loop so the
    /// wording can be asserted without driving a whole conversation.
    /// </summary>
    internal static string FormatExhaustedTurn(IReadOnlyList<string> narration, int maxToolIterations)
    {
        var notice =
            $"(Stopped after {maxToolIterations} rounds of tool calls without reaching a final answer. " +
            "Ask again, or narrow the question.)";

        if (narration.Count == 0)
        {
            return notice;
        }

        return string.Join(Environment.NewLine, narration) + Environment.NewLine + Environment.NewLine + notice;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Executing tool {ToolName}")]
    private partial void LogExecutingTool(string toolName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The turn used all {MaxToolIterations} tool-calling rounds; returning a partial answer")]
    private partial void LogIterationLimitReached(int maxToolIterations);

    private static string BuildToolContent(CallToolResult result)
    {
        var content = string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>()
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(content))
        {
            content = "(The tool returned no text.)";
        }

        if (result.IsError == true)
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


