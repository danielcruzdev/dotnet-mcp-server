using DotNetMcpServer.Server.Resources;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Completions;

/// <summary>
/// Answers <c>completion/complete</c> for the arguments this server can enumerate: the
/// workspace-relative paths taken by a prompt or by a resource template.
/// </summary>
/// <remarks>
/// Arguments with a fixed set of values — <c>audience</c> on <c>summarize_document</c>, for
/// one — are completed by the SDK from their <c>[AllowedValues]</c> attribute. What is left is
/// the case the SDK cannot know: values that come from the filesystem and change between one
/// request and the next.
/// </remarks>
internal static class WorkspaceCompletionHandler
{
    /// <summary>
    /// The spec caps a completion response at 100 values, and a client shows far fewer.
    /// </summary>
    private const int MaxValues = 100;

    /// <summary>Argument names that name a workspace document, wherever they appear.</summary>
    private static readonly HashSet<string> PathArguments = new(StringComparer.Ordinal) { "path" };

    public static ValueTask<CompleteResult> CompleteAsync(
        RequestContext<CompleteRequestParams> request,
        CancellationToken cancellationToken)
    {
        var argument = request.Params?.Argument;

        if (argument is null || !PathArguments.Contains(argument.Name) || !IsOurs(request.Params?.Ref))
        {
            return ValueTask.FromResult(Empty());
        }

        var services = request.Services
            ?? throw new InvalidOperationException("The request has no service provider.");

        var typed = argument.Value ?? string.Empty;

        var matches = services.GetRequiredService<WorkspaceResourceProvider>()
            .EnumerateDocuments()
            .Where(document => document.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return ValueTask.FromResult(new CompleteResult
        {
            Completion = new Completion
            {
                Values = [.. matches.Take(MaxValues)],
                Total = matches.Count,
                HasMore = matches.Count > MaxValues
            }
        });
    }

    /// <summary>
    /// Whether the reference is to something this server serves. Completing a path for
    /// somebody else's prompt would be guessing.
    /// </summary>
    private static bool IsOurs(Reference? reference)
    {
        return reference switch
        {
            PromptReference prompt => prompt.Name is "summarize_document",
            ResourceTemplateReference template =>
                template.Uri?.StartsWith("workspace://", StringComparison.Ordinal) == true,
            _ => false
        };
    }

    private static CompleteResult Empty()
    {
        return new CompleteResult { Completion = new Completion { Values = [], Total = 0, HasMore = false } };
    }
}
