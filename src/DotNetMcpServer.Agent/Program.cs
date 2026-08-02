using DotNetMcpServer.Agent.Config;
using DotNetMcpServer.Agent.Llm;
using DotNetMcpServer.Agent.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// The content root is the binary's own folder rather than the process working directory,
// so appsettings.json is found however the agent is launched.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Added last so the documented flat variables (OPENAI_API_KEY and friends) win over
// appsettings.json.
builder.Configuration.AddInMemoryCollection(FlatEnvironmentVariables.ReadProcessEnvironment());

builder.Services.AddAgentOptions(builder.Configuration);
builder.Services.AddSingleton<IPostConfigureOptions<McpSettings>>(
    _ => new McpSettingsSetup(AppContext.BaseDirectory, Directory.GetCurrentDirectory()));

// Resilience policies land in F2-04; this replaces the raw `new HttpClient()`.
builder.Services.AddHttpClient<OpenAiChatClient>();

builder.Services.AddSingleton<InteractiveAgentRunner>();
builder.Services.AddHostedService<AgentHostedService>();

await builder.Build().RunAsync();
