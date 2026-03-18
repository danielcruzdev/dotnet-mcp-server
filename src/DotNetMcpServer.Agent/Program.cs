using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using DotNetMcpServer.Agent.Mcp;
using DotNetMcpServer.Agent.Runtime;

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

            await using var mcpClient = await McpClient.StartAsync(settings.Mcp, cancellationTokenSource.Token);
            var runner = new InteractiveAgentRunner(settings.Runtime, openAiClient, mcpClient);

            await runner.RunAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Encerrando agente...");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Erro fatal: {exception.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ValidateConfiguration(AgentSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY não configurada. Defina no ambiente ou no appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(settings.OpenAI.Model))
        {
            throw new InvalidOperationException("Modelo OpenAI não configurado.");
        }
    }
}

