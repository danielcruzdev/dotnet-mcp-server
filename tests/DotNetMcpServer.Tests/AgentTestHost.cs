using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotNetMcpServer.Tests;

/// <summary>
/// Builds the agent's OpenAI client through the shipped registration, with the transport
/// swapped for a test double. Going through <c>AddOpenAiChatClient</c> rather than newing the
/// client up is the point: the resilience pipeline is part of what is under test.
/// </summary>
internal static class AgentTestHost
{
    public static ServiceProvider WithModel(HttpMessageHandler model)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<OpenAiSettings>>(Options.Create(new OpenAiSettings
        {
            ApiKey = "sk-test-key",
            BaseUrl = "https://openai.invalid/v1"
        }));

        services.AddOpenAiChatClient().ConfigurePrimaryHttpMessageHandler(() => model);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A model that fails the test if it is called at all.
    /// </summary>
    public sealed class UnusedModel : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The model was called, and this test expects it not to be.");
        }
    }

    /// <summary>
    /// A reader that never yields a line — a console with nobody typing at it.
    /// </summary>
    public sealed class BlockingReader : TextReader
    {
        private readonly ManualResetEventSlim _release;

        public BlockingReader(ManualResetEventSlim release)
        {
            _release = release;
        }

        public override string? ReadLine()
        {
            _release.Wait();
            return null;
        }
    }
}
