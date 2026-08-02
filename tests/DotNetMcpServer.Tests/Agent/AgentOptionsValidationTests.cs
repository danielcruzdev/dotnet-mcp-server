using DotNetMcpServer.Agent.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DotNetMcpServer.Tests.Agent;

/// <summary>
/// Configuration must fail at startup, not at the first request. These tests build a real
/// host over an in-memory configuration and start it, because <c>ValidateOnStart</c> is what
/// turns a binding error into a startup error.
/// </summary>
public sealed class AgentOptionsValidationTests
{
    /// <summary>
    /// Defaults are disabled deliberately: the host would otherwise read the machine's
    /// environment variables and this test would pass or fail depending on whether the
    /// developer happens to have <c>OPENAI_API_KEY</c> exported.
    /// </summary>
    private static IHost BuildHost(Dictionary<string, string?> configuration)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true
        });

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddAgentOptions(builder.Configuration);

        return builder.Build();
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["openAI:apiKey"] = "sk-test-key",
        ["openAI:model"] = "gpt-4o-mini",
        ["runtime:maxToolIterations"] = "6"
    };

    [Fact]
    public async Task A_missing_api_key_fails_at_startup()
    {
        var configuration = ValidConfiguration();
        configuration.Remove("openAI:apiKey");

        using var host = BuildHost(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));

        Assert.Contains("ApiKey", string.Join(" ", exception.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// Unlike the API key, the model has a sensible default, so leaving it unset is not a
    /// configuration error.
    /// </summary>
    [Fact]
    public async Task A_missing_model_falls_back_to_the_default()
    {
        var configuration = ValidConfiguration();
        configuration.Remove("openAI:model");

        using var host = BuildHost(configuration);

        await host.StartAsync(CancellationToken.None);

        var openAi = host.Services.GetRequiredService<IOptions<OpenAiSettings>>().Value;
        Assert.Equal("gpt-4o-mini", openAi.Model);

        await host.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// This value used to be silently clamped into range by the settings loader. Correcting a
    /// user's configuration without telling them hides the mistake, so it now fails loudly.
    /// </summary>
    [Fact]
    public async Task A_tool_iteration_limit_outside_the_supported_range_fails_instead_of_being_clamped()
    {
        var configuration = ValidConfiguration();
        configuration["runtime:maxToolIterations"] = "99";

        using var host = BuildHost(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));

        Assert.Contains("MaxToolIterations", string.Join(" ", exception.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_valid_configuration_starts_and_binds()
    {
        using var host = BuildHost(ValidConfiguration());

        await host.StartAsync(CancellationToken.None);

        var openAi = host.Services.GetRequiredService<IOptions<OpenAiSettings>>().Value;
        Assert.Equal("sk-test-key", openAi.ApiKey);
        Assert.Equal("gpt-4o-mini", openAi.Model);

        await host.StopAsync(CancellationToken.None);
    }
}
