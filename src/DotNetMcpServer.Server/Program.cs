using DotNetMcpServer.Server.Completions;
using DotNetMcpServer.Server.Logging;
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

// The client, not this configuration, decides how verbose the log stream it receives is, so
// every level has to reach the bridge for logging/setLevel to have anything to filter. The
// console keeps its own default and is unaffected.
builder.Logging.AddFilter<ClientLogBridge>(category: null, level: LogLevel.Trace);

builder.Services.AddSingleton(WorkspaceContext.Resolve(args));
builder.Services.AddSingleton<WorkspaceResourceProvider>();
builder.Services.AddSingleton<WorkspaceResourceSubscriptions>();
builder.Services.AddSingleton<ClientLogBridge>();
builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<ClientLogBridge>());

var mcpServer = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly()
    .WithListResourcesHandler(WorkspaceResourceHandlers.ListResourcesAsync)
    .WithReadResourceHandler(WorkspaceResourceHandlers.ReadResourceAsync)
    .WithSubscribeToResourcesHandler(WorkspaceResourceHandlers.SubscribeAsync)
    .WithUnsubscribeFromResourcesHandler(WorkspaceResourceHandlers.UnsubscribeAsync)
    .WithCompleteHandler(WorkspaceCompletionHandler.CompleteAsync);

// Registered on its own so the suppression covers this one call and nothing else.
#pragma warning disable MCP9005 // Logging is deprecated by 2026-07-28 (SEP-2577); see ClientLogBridge.
mcpServer.WithSetLoggingLevelHandler(ClientLogBridge.AttachOnSetLevelAsync);
#pragma warning restore MCP9005

// The SDK derives the resources capability from the handlers above, which tells a client that
// subscriptions work. Nothing tells it the list itself is watched, so that is declared here.
builder.Services.Configure<McpServerOptions>(options =>
{
    options.Capabilities ??= new ServerCapabilities();
    options.Capabilities.Resources ??= new ResourcesCapability();
    options.Capabilities.Resources.ListChanged = true;
});

await builder.Build().RunAsync();
