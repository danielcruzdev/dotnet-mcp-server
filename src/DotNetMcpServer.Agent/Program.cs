using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using DotNetMcpServer.Agent.Runtime;
using ModelContextProtocol.Client;

namespace DotNetMcpServer.Agent;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var settings = AgentSettingsLoader.Load(AppContext.BaseDirectory, Directory.GetCurrentDirectory());
            ValidateConfiguration(settings);

            using var httpClient = new HttpClient();
            var openAiClient = new OpenAiChatClient(httpClient, settings.OpenAI);

            // The server is launched as a compiled binary, never through `dotnet run`:
            // MSBuild writes to stdout, and stdout is the protocol channel.
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "dotnet-mcp-server",
                Command = settings.Mcp.Command,
                Arguments = settings.Mcp.ArgumentList,
                WorkingDirectory = settings.Mcp.WorkingDirectory,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["MCP_WORKSPACE_ROOT"] = settings.Mcp.WorkspaceRoot
                }
            });

            await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationTokenSource.Token);
            var runner = new InteractiveAgentRunner(settings.Runtime, openAiClient, mcpClient);

            await runner.RunAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Shutting down agent...");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Fatal error: {exception.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ValidateConfiguration(AgentSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not set. Configure it in the environment.");
        }

        if (string.IsNullOrWhiteSpace(settings.OpenAI.Model))
        {
            throw new InvalidOperationException("No OpenAI model configured.");
        }
    }
}
