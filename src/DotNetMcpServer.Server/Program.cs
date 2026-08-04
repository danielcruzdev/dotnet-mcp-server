using DotNetMcpServer.Server.Resources;
using DotNetMcpServer.Server.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol stream and nothing else. Every log line goes to stderr,
// otherwise a single log write corrupts the session for the client.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(WorkspaceContext.Resolve(args));
builder.Services.AddSingleton<WorkspaceResourceProvider>();
builder.Services.AddSingleton<WorkspaceResourceSubscriptions>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly()
    .WithListResourcesHandler(WorkspaceResourceHandlers.ListResourcesAsync)
    .WithReadResourceHandler(WorkspaceResourceHandlers.ReadResourceAsync)
    .WithSubscribeToResourcesHandler(WorkspaceResourceHandlers.SubscribeAsync)
    .WithUnsubscribeFromResourcesHandler(WorkspaceResourceHandlers.UnsubscribeAsync);

// The SDK derives the resources capability from the handlers above, which tells a client that
// subscriptions work. Nothing tells it the list itself is watched, so that is declared here.
builder.Services.Configure<McpServerOptions>(options =>
{
    options.Capabilities ??= new ServerCapabilities();
    options.Capabilities.Resources ??= new ResourcesCapability();
    options.Capabilities.Resources.ListChanged = true;
});

await builder.Build().RunAsync();
