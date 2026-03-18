using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Shared.Json;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Agent.Llm;

public sealed class OpenAiChatClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiSettings _settings;

    public OpenAiChatClient(HttpClient httpClient, OpenAiSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<AssistantTurn> CompleteAsync(IReadOnlyList<JsonObject> messages, IReadOnlyList<McpToolDefinition> tools, CancellationToken cancellationToken)
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
            throw new InvalidOperationException($"Falha na OpenAI ({(int)response.StatusCode}): {responseBody}");
        }

        return ParseAssistantTurn(responseBody);
    }

    private string BuildEndpoint()
    {
        return $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions";
    }

    private JsonObject BuildPayload(IReadOnlyList<JsonObject> messages, IReadOnlyList<McpToolDefinition> tools)
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
                        ["parameters"] = tool.InputSchema.DeepClone()
                    }
                });
            }

            payload["tools"] = toolsArray;
            payload["tool_choice"] = "auto";
        }

        return payload;
    }

    private static AssistantTurn ParseAssistantTurn(string responseBody)
    {
        var root = JsonNode.Parse(responseBody)?.AsObject()
            ?? throw new InvalidDataException("Resposta inválida da OpenAI.");

        var assistantMessage = root["choices"]?[0]?["message"] as JsonObject
            ?? throw new InvalidDataException("Resposta sem mensagem da OpenAI.");

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

    private static List<AssistantToolCall> ParseToolCalls(JsonArray? rawToolCalls)
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
                Console.Error.WriteLine($"[agent] Falha ao interpretar argumentos da tool '{functionName}': {ex.Message}");
                arguments = new JsonObject();
            }

            toolCalls.Add(new AssistantToolCall(id, functionName, arguments));
        }

        return toolCalls;
    }
}

public sealed record AssistantTurn(string Content, List<AssistantToolCall> ToolCalls);

public sealed record AssistantToolCall(string Id, string Name, JsonObject Arguments);


