using DotNetMcpServer.Agent.Config;

namespace DotNetMcpServer.Tests.Agent;

/// <summary>
/// The agent documents flat environment variable names (<c>OPENAI_API_KEY</c>) in
/// <c>.env.example</c> and <c>docs/INSTALL.md</c>, while <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// addresses settings by section path. These tests pin the translation between the two.
/// </summary>
public sealed class FlatEnvironmentVariablesTests
{
    [Fact]
    public void Api_key_maps_onto_the_openai_section()
    {
        var environment = new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = "sk-test-key"
        };

        var configuration = FlatEnvironmentVariables.ToConfiguration(environment);

        Assert.Equal("sk-test-key", configuration["openAI:apiKey"]);
    }
}
