using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using DotNetMcpServer.Server.Resources;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Prompts;

/// <summary>
/// Reusable study and analysis templates, served through <c>prompts/list</c> and
/// <c>prompts/get</c>.
/// </summary>
/// <remarks>
/// A prompt is a template the <em>user</em> chooses, not something the model calls. The
/// server's job is to turn a couple of arguments into messages worth sending — which for a
/// workspace server means attaching the document being discussed, so the model is not asked to
/// summarise something it cannot see.
/// </remarks>
[McpServerPromptType]
public static class WorkspacePrompts
{
    private const int MinimumHoursPerWeek = 1;
    private const int MaximumHoursPerWeek = 40;

    [McpServerPrompt(Name = "summarize_document", Title = "Summarise a workspace document")]
    [Description("Asks for a summary of a workspace document, with the document itself attached.")]
    public static async Task<GetPromptResult> SummarizeDocument(
        WorkspaceResourceProvider provider,
        [Description("Workspace-relative path of the document, e.g. docs/INSTALL.md")] string path,
        [Description("Who the summary is written for.")]
        [AllowedValues("engineer", "reviewer", "newcomer")] string audience = "engineer",
        CancellationToken cancellationToken = default)
    {
        var uri = WorkspaceResourceProvider.ToUri(RequireArgument(path, nameof(path)));

        var document = await provider.TryReadAsync(uri, cancellationToken).ConfigureAwait(false)
            ?? throw new McpProtocolException($"Not a workspace document: {path}", McpErrorCode.InvalidParams);

        var instruction = audience switch
        {
            "reviewer" => "Summarise the attached document for a reviewer: what it decides, what it leaves open, "
                + "and anything that looks inconsistent.",
            "newcomer" => "Summarise the attached document for someone seeing this project for the first time. "
                + "Explain the vocabulary it assumes.",
            _ => "Summarise the attached document for an engineer who will work on it: what it covers, "
                + "what it requires, and what it does not say."
        };

        return new GetPromptResult
        {
            Description = $"Summary of {path} for a {audience}.",
            Messages =
            [
                new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = instruction } },
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new EmbeddedResourceBlock { Resource = document }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "study_plan", Title = "Plan a study topic")]
    [Description("Builds a study plan for a topic, sized to the time actually available.")]
    public static GetPromptResult StudyPlan(
        [Description("What to study, e.g. 'MCP resource templates'")] string topic,
        [Description("Hours available per week, 1 to 40.")] int hoursPerWeek = 5)
    {
        RequireArgument(topic, nameof(topic));

        if (hoursPerWeek is < MinimumHoursPerWeek or > MaximumHoursPerWeek)
        {
            throw new McpProtocolException(
                $"'hoursPerWeek' must be between {MinimumHoursPerWeek} and {MaximumHoursPerWeek}.",
                McpErrorCode.InvalidParams);
        }

        var instruction = string.Create(
            CultureInfo.InvariantCulture,
            $"""
             Build a study plan for: {topic}.

             I have {hoursPerWeek} hours a week. Break the topic into weekly milestones that fit that
             budget, name one thing to build for each milestone, and say what I should be able to
             explain at the end of it. Prefer primary sources — specifications and reference
             implementations — over tutorials.
             """);

        return new GetPromptResult
        {
            Description = $"Study plan for {topic} at {hoursPerWeek} h/week.",
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = instruction } }]
        };
    }

    /// <summary>
    /// Prompt arguments arrive as untrusted strings; an empty one is a client mistake worth
    /// naming rather than a template rendered with a hole in it.
    /// </summary>
    private static string RequireArgument(string value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new McpProtocolException($"'{name}' is required.", McpErrorCode.InvalidParams)
            : value;
    }
}
