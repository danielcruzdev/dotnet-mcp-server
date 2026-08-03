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
        var provider = Provider(request);

        return ValueTask.FromResult(provider.ListPage(request.Params?.Cursor));
    }

    public static async ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken)
    {
        var uri = request.Params?.Uri;

        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new McpProtocolException("'uri' is required.", McpErrorCode.InvalidParams);
        }

        var contents = await Provider(request).TryReadAsync(uri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpProtocolException($"Unknown resource URI: {uri}", McpErrorCode.InvalidParams);

        return new ReadResourceResult { Contents = [contents] };
    }

    private static WorkspaceResourceProvider Provider<TParams>(RequestContext<TParams> request)
    {
        return request.Services?.GetRequiredService<WorkspaceResourceProvider>()
            ?? throw new InvalidOperationException("The request has no service provider.");
    }
}
