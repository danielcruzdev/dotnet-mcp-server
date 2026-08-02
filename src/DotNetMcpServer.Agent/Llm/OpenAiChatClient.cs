using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Json;

namespace DotNetMcpServer.Agent.Llm;

public sealed partial class OpenAiChatClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiSettings _settings;
    private readonly ILogger<OpenAiChatClient> _logger;

    public OpenAiChatClient(HttpClient httpClient, IOptions<OpenAiSettings> settings, ILogger<OpenAiChatClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AssistantTurn> CompleteAsync(IReadOnlyList<JsonObject> messages, IList<McpClientTool> tools, CancellationToken cancellationToken)
    {
        var payload = BuildPayload(messages, tools);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint())
        {
            Content = JsonContent.Create(payload, options: JsonDefaults.SerializerOptions)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI request failed ({(int)response.StatusCode}): {responseBody}");
        }

        return ParseAssistantTurn(responseBody);
    }

    private string BuildEndpoint()
    {
        return $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions";
    }

    private JsonObject BuildPayload(IReadOnlyList<JsonObject> messages, IList<McpClientTool> tools)
    {
        var messageArray = new JsonArray();
        foreach (var message in messages)
        {
            messageArray.Add(message.DeepClone());
        }

        var payload = new JsonObject
        {
            ["model"] = _settings.Model,
            ["temperature"] = _settings.Temperature,
            ["messages"] = messageArray
        };

        if (tools.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var tool in tools)
            {
                toolsArray.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                            // The SDK exposes the tool's schema as a JsonElement; OpenAI wants it
                        // inline under "parameters".
                        ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText())
                    }
                });
            }

            payload["tools"] = toolsArray;
            payload["tool_choice"] = "auto";
        }

        return payload;
    }

    private AssistantTurn ParseAssistantTurn(string responseBody)
    {
        var root = JsonNode.Parse(responseBody)?.AsObject()
            ?? throw new InvalidDataException("The OpenAI response was not a JSON object.");

        var assistantMessage = root["choices"]?[0]?["message"] as JsonObject
            ?? throw new InvalidDataException("The OpenAI response carried no assistant message.");

        var content = ParseContent(assistantMessage["content"]);
        var toolCalls = ParseToolCalls(assistantMessage["tool_calls"] as JsonArray);

        return new AssistantTurn(content, toolCalls);
    }

    private static string ParseContent(JsonNode? rawContent)
    {
        if (rawContent is null)
        {
            return string.Empty;
        }

        if (rawContent is JsonValue value && value.TryGetValue<string>(out var directText))
        {
            return directText;
        }

        if (rawContent is not JsonArray arrayContent)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in arrayContent)
        {
            var itemObject = item as JsonObject;
            var text = itemObject?["text"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not parse the arguments for tool {ToolName}; falling back to an empty object")]
    private partial void LogUnparsableToolArguments(Exception exception, string toolName);

    private List<AssistantToolCall> ParseToolCalls(JsonArray? rawToolCalls)
    {
        var toolCalls = new List<AssistantToolCall>();
        if (rawToolCalls is null)
        {
            return toolCalls;
        }

        foreach (var rawToolCall in rawToolCalls)
        {
            var objectNode = rawToolCall as JsonObject;
            var id = objectNode?["id"]?.GetValue<string>();
            var functionNode = objectNode?["function"] as JsonObject;
            var functionName = functionNode?["name"]?.GetValue<string>();
            var rawArguments = functionNode?["arguments"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(functionName))
            {
                continue;
            }

            JsonObject arguments;
            try
            {
                arguments = JsonNode.Parse(rawArguments ?? "{}") as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                LogUnparsableToolArguments(ex, functionName);
                arguments = new JsonObject();
            }

            toolCalls.Add(new AssistantToolCall(id, functionName, arguments));
        }

        return toolCalls;
    }
}

public sealed record AssistantTurn(string Content, List<AssistantToolCall> ToolCalls);

public sealed record AssistantToolCall(string Id, string Name, JsonObject Arguments);


