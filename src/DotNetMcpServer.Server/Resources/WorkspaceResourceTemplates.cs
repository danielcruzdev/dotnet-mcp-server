using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Resources;

/// <summary>
/// Resource templates: addresses a client can construct rather than pick from a list.
/// </summary>
/// <remarks>
/// <c>resources/list</c> can only ever offer whole documents, one URI each. A template
/// advertises a shape — here an RFC 6570 URI with two simple variables and one reserved
/// expansion — so a client can address a span of a document it has already seen without the
/// server having to enumerate every span in advance.
/// <para>
/// The reserved expansion (<c>{+path}</c>) is what lets the path variable carry <c>/</c>:
/// a plain <c>{path}</c> stops at the first separator and would only ever match documents in
/// the workspace root.
/// </para>
/// </remarks>
[McpServerResourceType]
public static class WorkspaceResourceTemplates
{
    [McpServerResource(
        UriTemplate = "workspace://excerpt/{start}-{end}/{+path}",
        Name = "workspace_excerpt",
        Title = "Workspace document excerpt",
        MimeType = "text/plain")]
    [Description(
        "An inclusive, 1-based line range of a workspace document. "
        + "Example: workspace://excerpt/10-25/docs/INSTALL.md")]
    public static async Task<ResourceContents> ReadExcerpt(
        WorkspaceResourceProvider provider,
        RequestContext<ReadResourceRequestParams> request,
        int start,
        int end,
        string path,
        CancellationToken cancellationToken)
    {
        var text = await provider.ReadExcerptAsync(path, start, end, cancellationToken).ConfigureAwait(false);

        return new TextResourceContents
        {
            Uri = request.Params?.Uri ?? $"workspace://excerpt/{start}-{end}/{path}",
            MimeType = WorkspaceResourceProvider.MimeTypeFor(path),
            Text = text
        };
    }
}
