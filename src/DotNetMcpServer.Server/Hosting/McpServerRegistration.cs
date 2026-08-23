using DotNetMcpServer.Server.Completions;
using DotNetMcpServer.Server.Resources;
using DotNetMcpServer.Server.Workspace;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// Everything both transports register. The transport itself is added by the caller — it is
/// the only thing the stdio and HTTP hosts disagree about in the MCP builder chain.
/// </summary>
/// <remarks>
/// This exists so the capability surface cannot drift between the two transports. A tool added
/// to one and forgotten in the other would make <c>F4-08</c>'s both-transports theory fail for
/// a reason that has nothing to do with the transport.
/// </remarks>
internal static class McpServerRegistration
{
    /// <summary>
    /// Registers the workspace services, the tool/resource/prompt surface, and every request
    /// handler this server answers.
    /// </summary>
    public static IMcpServerBuilder AddWorkspaceMcpServer(this IServiceCollection services, string[] args)
    {
        services.AddSingleton(WorkspaceContext.Resolve(args));
        services.AddSingleton<WorkspaceResourceProvider>();
        services.AddSingleton<WorkspaceResourceSubscriptions>();

        var mcpServer = services
            .AddMcpServer()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly()
            .WithListResourcesHandler(WorkspaceResourceHandlers.ListResourcesAsync)
            .WithReadResourceHandler(WorkspaceResourceHandlers.ReadResourceAsync)
            .WithSubscribeToResourcesHandler(WorkspaceResourceHandlers.SubscribeAsync)
            .WithUnsubscribeFromResourcesHandler(WorkspaceResourceHandlers.UnsubscribeAsync)
            .WithCompleteHandler(WorkspaceCompletionHandler.CompleteAsync);

        // The SDK derives the resources capability from the handlers above, which tells a
        // client that subscriptions work. Nothing tells it the list itself is watched, so that
        // is declared here.
        services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Resources ??= new ResourcesCapability();
            options.Capabilities.Resources.ListChanged = true;
        });

        return mcpServer;
    }
}
