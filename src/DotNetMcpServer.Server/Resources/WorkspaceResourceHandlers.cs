using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Resources;

/// <summary>
/// The <c>resources/list</c> and <c>resources/read</c> handlers.
/// </summary>
/// <remarks>
/// These are request handlers rather than <c>[McpServerResource]</c> methods because the set
/// of resources is discovered from the filesystem at request time — an attributed resource
/// declares a fixed URI, and there is no fixed set of workspace documents.
/// </remarks>
internal static class WorkspaceResourceHandlers
{
    public static ValueTask<ListResourcesResult> ListResourcesAsync(
        RequestContext<ListResourcesRequestParams> request,
        CancellationToken cancellationToken)
    {
        // The client has shown interest in the resource list, so start watching for changes
        // to it — this is what makes the advertised listChanged capability true.
        Service<WorkspaceResourceSubscriptions>(request).EnsureWatching(request.Server);

        return ValueTask.FromResult(Service<WorkspaceResourceProvider>(request).ListPage(request.Params?.Cursor));
    }

    public static ValueTask<EmptyResult> SubscribeAsync(
        RequestContext<SubscribeRequestParams> request,
        CancellationToken cancellationToken)
    {
        Service<WorkspaceResourceSubscriptions>(request).Subscribe(request.Server, RequiredUri(request.Params?.Uri));

        return ValueTask.FromResult(new EmptyResult());
    }

    public static ValueTask<EmptyResult> UnsubscribeAsync(
        RequestContext<UnsubscribeRequestParams> request,
        CancellationToken cancellationToken)
    {
        Service<WorkspaceResourceSubscriptions>(request).Unsubscribe(RequiredUri(request.Params?.Uri));

        return ValueTask.FromResult(new EmptyResult());
    }

    public static async ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken)
    {
        var uri = RequiredUri(request.Params?.Uri);

        var contents = await Service<WorkspaceResourceProvider>(request)
                .TryReadAsync(uri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpProtocolException($"Unknown resource URI: {uri}", McpErrorCode.InvalidParams);

        return new ReadResourceResult { Contents = [contents] };
    }

    private static string RequiredUri(string? uri)
    {
        return string.IsNullOrWhiteSpace(uri)
            ? throw new McpProtocolException("'uri' is required.", McpErrorCode.InvalidParams)
            : uri;
    }

    private static TService Service<TService>(MessageContext request)
        where TService : notnull
    {
        var services = request.Services
            ?? throw new InvalidOperationException("The request has no service provider.");

        return services.GetRequiredService<TService>();
    }
}
