using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetMcpServer.Agent.Config;

public static class AgentOptionsRegistration
{
    /// <summary>
    /// Binds and validates the agent's three configuration sections.
    /// <c>ValidateOnStart</c> is what moves a configuration mistake from the first request to
    /// startup, where it is cheap to diagnose.
    /// </summary>
    public static IServiceCollection AddAgentOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OpenAiSettings>()
            .Bind(configuration.GetSection("openAI"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<McpSettings>()
            .Bind(configuration.GetSection("mcp"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AgentRuntimeSettings>()
            .Bind(configuration.GetSection("runtime"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
