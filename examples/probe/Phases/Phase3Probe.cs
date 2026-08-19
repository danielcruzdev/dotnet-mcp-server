using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using DotNetMcpServer.Server.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Probe.Phases;

/// <summary>
/// Phase 3 — every MCP capability, not just tools.
/// </summary>
/// <remarks>
/// <para>
/// Two capabilities are shown in two states on purpose. <c>resources/subscribe</c> and
/// <c>logging/setLevel</c> were removed by revision <c>2026-07-28</c> (SEP-2575, SEP-2577), so
/// each is probed on <c>2025-11-25</c> — the revision shipping clients negotiate — and then on
/// the SDK's default, where the server is expected to refuse. The refusal is not a defect; it
/// is the phase's most useful finding, and printing it is the point.
/// </para>
/// <para>
/// Read-only checks run against <c>examples/workspace/</c>, so what is printed is real content.
/// Checks that write a note or edit a document build their own temp workspace instead — the
/// alternative is a probe that leaves untracked files in the repository every time it runs.
/// </para>
/// </remarks>
internal static class Phase3Probe
{
    /// <summary>The last revision carrying <c>resources/subscribe</c> and <c>logging/setLevel</c>.</summary>
    private const string LegacyProtocol = "2025-11-25";

    /// <summary>A document that exists in <c>examples/workspace/</c>.</summary>
    private const string SampleDocument = "dotnet-concepts.md";

    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(20);

    public static IReadOnlyList<string> Capabilities { get; } =
    [
        "resources",
        "templates",
        "subscriptions",
        "prompts",
        "completion",
        "logging",
        "progress",
        "structured",
        "elicitation",
        "sampling"
    ];

    public static async Task RunAsync(Report report, string? capability, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> selected = capability is null ? Capabilities : [capability];

        foreach (var name in selected)
        {
            report.Section($"Phase 3 - {name}");

            switch (name)
            {
                case "resources":
                    await ResourcesAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "templates":
                    await TemplatesAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "subscriptions":
                    await SubscriptionsAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "prompts":
                    await PromptsAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "completion":
                    await CompletionAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "logging":
                    await LoggingAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "progress":
                    await ProgressAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "structured":
                    await StructuredAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "elicitation":
                    await ElicitationAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                case "sampling":
                    await SamplingAsync(report, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    report.Fail($"'{name}' is not a Phase 3 capability.");
                    break;
            }
        }
    }

    private static async Task ResourcesAsync(Report report, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(ServerBinary.ExampleWorkspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("capabilities.resources", client.ServerCapabilities.Resources is null ? "(absent)" : "advertised");
        report.Value("resources/list", $"{resources.Count} documents");

        foreach (var resource in resources.OrderBy(resource => resource.Uri, StringComparer.Ordinal))
        {
            report.Item($"{resource.Uri,-46}{resource.MimeType,-16}{resource.ProtocolResource.Size} bytes");
        }

        var read = await client.ReadResourceAsync($"workspace://file/{SampleDocument}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var contents = read.Contents.OfType<TextResourceContents>().Single();

        report.Value("resources/read", contents.Uri);

        foreach (var line in contents.Text.Split('\n').Take(4))
        {
            report.Item(line.TrimEnd());
        }

        report.Check(resources.Count > 0, "workspace documents are listed as first-class resources");
        report.Check(
            resources.All(resource => !string.IsNullOrWhiteSpace(resource.MimeType)),
            "every resource carries a uri and a mime type");
        report.Check(contents.Text.Length > 0, "a resource reads back its text");
    }

    private static async Task TemplatesAsync(Report report, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(ServerBinary.ExampleWorkspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var templates = await client.ListResourceTemplatesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var template in templates)
        {
            report.Item($"{template.Name,-22}{template.UriTemplate}");
        }

        var uri = $"workspace://excerpt/1-3/{SampleDocument}";
        var excerpt = await client.ReadResourceAsync(uri, cancellationToken: cancellationToken).ConfigureAwait(false);
        var contents = excerpt.Contents.OfType<TextResourceContents>().Single();

        report.Value("resources/read", uri);

        foreach (var line in contents.Text.Split('\n'))
        {
            report.Item(line.TrimEnd());
        }

        var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Check(templates.Count > 0, "an RFC 6570 template is advertised");
        report.Check(
            contents.Text.Split('\n').Length == 3,
            "the template expands into the line range it was asked for");
        report.Check(
            !resources.Any(resource => templates.Any(template => template.Name == resource.Name)),
            "a template is not a resource, so it stays out of resources/list");
    }

    private static async Task SubscriptionsAsync(Report report, CancellationToken cancellationToken)
    {
        var workspace = CreateTempWorkspace("subs");

        try
        {
            var document = Path.Combine(workspace, "alpha.md");
            await File.WriteAllTextAsync(document, "# Alpha\n", cancellationToken).ConfigureAwait(false);

            var updates = Channel.CreateUnbounded<string>();

            await using (var client = await ConnectAsync(
                workspace,
                Handlers(NotificationMethods.ResourceUpdatedNotification, notification =>
                {
                    var uri = notification.Params?
                        .Deserialize<ResourceUpdatedNotificationParams>(McpJsonUtilities.DefaultOptions)?.Uri;

                    if (uri is not null)
                    {
                        updates.Writer.TryWrite(uri);
                    }
                }),
                LegacyProtocol,
                cancellationToken).ConfigureAwait(false))
            {
                await client.SubscribeToResourceAsync("workspace://file/alpha.md", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await File.WriteAllTextAsync(document, "# Alpha\nEdited by the probe.\n", cancellationToken)
                    .ConfigureAwait(false);

                var uri = await NextAsync(updates.Reader, cancellationToken).ConfigureAwait(false);

                report.Value($"on {LegacyProtocol}", "resources/subscribe accepted");
                report.Value("notifications/.../updated", uri);
                report.Check(
                    uri == "workspace://file/alpha.md",
                    "editing a subscribed document notifies the client");
            }

            await using (var client = await ConnectAsync(workspace, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
            {
                report.Note("SEP-2575 removed resources/subscribe on 2026-07-28; the refusal below is the spec, not a bug.");

                try
                {
                    await client.SubscribeToResourceAsync("workspace://file/alpha.md", cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    report.Fail("the current revision still accepted a method the spec removed");
                }
                catch (McpException exception)
                {
                    report.Value("refusal", exception.Message);
                    report.Check(
                        exception.Message.Contains("subscriptions/listen", StringComparison.Ordinal),
                        "the current revision refuses it and names its replacement");
                }
            }
        }
        finally
        {
            DeleteTempWorkspace(workspace);
        }
    }

    private static async Task PromptsAsync(Report report, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(ServerBinary.ExampleWorkspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("capabilities.prompts", client.ServerCapabilities.Prompts is null ? "(absent)" : "advertised");
        report.Value("prompts/list", $"{prompts.Count} prompts");

        foreach (var prompt in prompts.OrderBy(prompt => prompt.Name, StringComparer.Ordinal))
        {
            var arguments = prompt.ProtocolPrompt.Arguments ?? [];
            var rendered = arguments.Select(argument => argument.Required == true ? argument.Name : $"[{argument.Name}]");

            report.Item($"{prompt.Name}({string.Join(", ", rendered)})");
        }

        var result = await client.GetPromptAsync(
            "summarize_document",
            new Dictionary<string, object?> { ["path"] = SampleDocument, ["audience"] = "newcomer" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("prompts/get", $"summarize_document -> {result.Messages.Count} messages");

        foreach (var message in result.Messages)
        {
            report.Item(message.Content switch
            {
                TextContentBlock text => $"{message.Role}: {Truncate(text.Text)}",
                EmbeddedResourceBlock embedded => $"{message.Role}: embedded {embedded.Resource.Uri}",
                _ => $"{message.Role}: {message.Content.GetType().Name}"
            });
        }

        report.Check(prompts.Count > 0, "reusable prompts are advertised with their arguments");
        report.Check(
            result.Messages.Any(message => message.Content is EmbeddedResourceBlock),
            "a prompt attaches the document it asks about, rather than naming it");
    }

    private static async Task CompletionAsync(Report report, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(ServerBinary.ExampleWorkspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        report.Value("capabilities.completions", client.ServerCapabilities.Completions is null ? "(absent)" : "advertised");

        var paths = await client.CompleteAsync(
            new PromptReference { Name = "summarize_document" },
            "path",
            "dot",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("completion/complete", "summarize_document.path = 'dot'");

        foreach (var value in paths.Completion.Values)
        {
            report.Item(value);
        }

        var audiences = await client.CompleteAsync(
            new PromptReference { Name = "summarize_document" },
            "audience",
            string.Empty,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("completion/complete", "summarize_document.audience = ''");

        foreach (var value in audiences.Completion.Values)
        {
            report.Item(value);
        }

        report.Check(
            paths.Completion.Values.All(value => value.StartsWith("dot", StringComparison.Ordinal)),
            "a path argument completes from the documents the server can actually read");
        report.Check(
            audiences.Completion.Values.Count > 0,
            "a fixed argument completes from its allowed values, which the SDK supplies");
    }

    // MCP9005: Logging is deprecated by revision 2026-07-28 (SEP-2577), and setLevel was removed
    // outright. Probed anyway, for the same reason the server implements it — every shipping
    // client negotiates a revision where it works. Scoped to this method so the compiler points
    // here when the SDK drops it.
#pragma warning disable MCP9005
    private static async Task LoggingAsync(Report report, CancellationToken cancellationToken)
    {
        var messages = Channel.CreateUnbounded<LoggingMessageNotificationParams>();

        await using (var client = await ConnectAsync(
            ServerBinary.ExampleWorkspace,
            Handlers(NotificationMethods.LoggingMessageNotification, notification =>
            {
                var message = notification.Params?
                    .Deserialize<LoggingMessageNotificationParams>(McpJsonUtilities.DefaultOptions);

                if (message is not null)
                {
                    messages.Writer.TryWrite(message);
                }
            }),
            LegacyProtocol,
            cancellationToken).ConfigureAwait(false))
        {
            report.Value("capabilities.logging", client.ServerCapabilities.Logging is null ? "(absent)" : "advertised");

            await client.SetLoggingLevelAsync(LoggingLevel.Info, cancellationToken: cancellationToken).ConfigureAwait(false);

            await client.CallToolAsync(
                "read_text_file",
                new Dictionary<string, object?> { ["path"] = SampleDocument },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var message = await NextAsync(messages.Reader, cancellationToken).ConfigureAwait(false);

            report.Value($"on {LegacyProtocol}", "logging/setLevel accepted");
            report.Value("notifications/message", $"[{message.Level}] {message.Logger}");
            report.Item(Truncate(message.Data.ToString() ?? string.Empty));

            report.Check(
                message.Logger?.StartsWith("DotNetMcpServer.", StringComparison.Ordinal) == true,
                "only this server's own log categories are forwarded, so the bridge terminates");
        }

        await using (var client = await ConnectAsync(ServerBinary.ExampleWorkspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            report.Note("SEP-2577 moved the level onto per-request _meta on 2026-07-28.");

            try
            {
                await client.SetLoggingLevelAsync(LoggingLevel.Info, cancellationToken: cancellationToken).ConfigureAwait(false);

                report.Fail("the current revision still accepted a method the spec removed");
            }
            catch (McpException exception)
            {
                report.Value("refusal", exception.Message);
                report.Check(
                    exception.Message.Contains("logLevel", StringComparison.Ordinal),
                    "the current revision refuses it and names its replacement");
            }
        }
    }
#pragma warning restore MCP9005

    private static async Task ProgressAsync(Report report, CancellationToken cancellationToken)
    {
        var reports = Channel.CreateUnbounded<ProgressNotificationParams>();

        // Collected through a handler bound to the method, not the IProgress<T> overload of
        // CallToolAsync: the SDK forgets that registration when the response arrives, and a
        // report read but not yet dispatched is dropped.
        await using var client = await ConnectAsync(
            ServerBinary.ExampleWorkspace,
            Handlers(NotificationMethods.ProgressNotification, notification =>
            {
                var progress = notification.Params?
                    .Deserialize<ProgressNotificationParams>(McpJsonUtilities.DefaultOptions);

                if (progress is not null)
                {
                    reports.Writer.TryWrite(progress);
                }
            }),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var token = new ProgressToken("probe-" + Guid.NewGuid().ToString("N"));

        var result = await client.CallToolAsync(
            "scan_workspace",
            options: new RequestOptions { ProgressToken = token },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var scan = Structured<WorkspaceScan>(result);

        report.Value("scan_workspace", $"{scan?.Documents} documents, {scan?.Lines} lines, {scan?.Characters} chars");

        // The count comes from the answer rather than from a literal: examples/workspace/ can
        // gain documents, and a probe that hard-codes 4 rots the moment one is added.
        var expected = scan?.Documents ?? 0;
        var received = new List<ProgressNotificationValue>();

        for (var index = 0; index < expected; index++)
        {
            received.Add((await NextAsync(reports.Reader, cancellationToken).ConfigureAwait(false)).Progress);
        }

        foreach (var progress in received.OrderBy(progress => progress.Progress))
        {
            report.Item($"{progress.Progress}/{progress.Total}  {progress.Message}");
        }

        report.Check(expected > 0, "a long-running tool reports what it walked");
        report.Check(
            received.Count == expected,
            "every document the tool walked owes exactly one report, the last one included");
    }

    private static async Task StructuredAsync(Report report, CancellationToken cancellationToken)
    {
        await using var client = await ConnectAsync(ServerBinary.ExampleWorkspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var tool in tools.OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            var annotations = tool.ProtocolTool.Annotations;
            var hints = annotations is null
                ? "(none)"
                : $"readOnly={Render(annotations.ReadOnlyHint)} destructive={Render(annotations.DestructiveHint)} " +
                  $"idempotent={Render(annotations.IdempotentHint)} openWorld={Render(annotations.OpenWorldHint)}";

            report.Item($"{tool.Name,-22}schema={(tool.ProtocolTool.OutputSchema is null ? "no " : "yes")}  {hints}");
        }

        var result = await client.CallToolAsync(
            "calculate_expression",
            new Dictionary<string, object?> { ["expression"] = "(1200 + 350) / 5" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var calculation = Structured<CalculationResult>(result);

        report.Value("structuredContent", result.StructuredContent?.ToString() ?? "(none)");
        report.Value("deserialized", $"{calculation?.Expression} = {calculation?.Result}");

        var prose = await client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = SampleDocument },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Check(calculation?.Result == 310m, "a tool whose answer is data returns it as data");
        report.Check(
            tools.Where(tool => tool.Name is "calculate_expression" or "get_current_datetime" or "scan_workspace")
                .All(tool => tool.ProtocolTool.OutputSchema is not null),
            "the tools that answer with data publish an output schema");
        report.Check(
            prose.StructuredContent is null,
            "the tools that answer with prose do not, so a client is not made to unescape a document");
        report.Check(
            tools.All(tool => tool.ProtocolTool.Annotations is not null),
            "every tool states its annotations, including the ones that look like defaults");
    }

    private static async Task ElicitationAsync(Report report, CancellationToken cancellationToken)
    {
        var workspace = CreateTempWorkspace("elicit");

        try
        {
            ElicitRequestParams? asked = null;

            var options = new McpClientOptions
            {
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (request, token) =>
                    {
                        asked = request;

                        return ValueTask.FromResult(new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement>
                            {
                                ["title"] = JsonSerializer.SerializeToElement("Newline framing")
                            }
                        });
                    }
                }
            };

            await using var client = await ConnectAsync(workspace, options, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var result = await client.CallToolAsync(
                "append_study_note",
                new Dictionary<string, object?> { ["note"] = "Messages are delimited by newlines." },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            report.Value("tools/call", "append_study_note(note) — no title given");
            report.Value("elicitation/create", asked?.Message ?? "(not asked)");
            report.Value("requestedSchema", string.Join(", ", asked?.RequestedSchema?.Properties.Keys ?? []));
            report.Value("answered", "Newline framing");

            var notes = await ReadNotesAsync(workspace, cancellationToken).ConfigureAwait(false);

            foreach (var line in notes.Split('\n').Take(3))
            {
                report.Item(line.TrimEnd());
            }

            report.Check(result.IsError != true && asked is not null, "a tool asks the user for the argument it lacks");
            report.Check(
                notes.Contains("## Newline framing", StringComparison.Ordinal),
                "the user's answer reaches the note on disk");
        }
        finally
        {
            DeleteTempWorkspace(workspace);
        }
    }

    // MCP9005: Sampling is deprecated by 2026-07-28 (SEP-2577) but, unlike subscribe and
    // setLevel, not removed — its round trip still completes on the default revision.
#pragma warning disable MCP9005
    private static async Task SamplingAsync(Report report, CancellationToken cancellationToken)
    {
        var workspace = CreateTempWorkspace("sampling");

        try
        {
            CreateMessageRequestParams? asked = null;

            var options = new McpClientOptions
            {
                Handlers = new McpClientHandlers
                {
                    SamplingHandler = (request, progress, token) =>
                    {
                        asked = request;

                        return ValueTask.FromResult(new CreateMessageResult
                        {
                            Model = "probe-stub",
                            Role = Role.Assistant,
                            // Deliberately badly behaved: a heading built from this verbatim
                            // would corrupt every note after it.
                            Content = [new TextContentBlock { Text = "\"Newline Framing\"\nAnd some commentary." }]
                        });
                    }
                }
            };

            await using var client = await ConnectAsync(workspace, options, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var result = await client.CallToolAsync(
                "append_study_note",
                new Dictionary<string, object?> { ["note"] = "Messages are delimited by newlines." },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            report.Note("No elicitation handler is registered, so the server falls back to the client's model.");
            report.Value("sampling/createMessage", asked?.SystemPrompt ?? "(not asked)");
            report.Value("maxTokens", asked?.MaxTokens.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            report.Value("model answered", "\"Newline Framing\" + a second line");

            var notes = await ReadNotesAsync(workspace, cancellationToken).ConfigureAwait(false);

            foreach (var line in notes.Split('\n').Take(3))
            {
                report.Item(line.TrimEnd());
            }

            report.Check(result.IsError != true && asked is not null, "the server borrows the client's model to name a note");
            report.Check(
                notes.Contains("## Newline Framing", StringComparison.Ordinal)
                    && !notes.Contains("commentary", StringComparison.Ordinal),
                "the suggestion is sanitised before it becomes a markdown heading");
        }
        finally
        {
            DeleteTempWorkspace(workspace);
        }
    }
#pragma warning restore MCP9005

    private static McpClientOptions Handlers(string method, Action<JsonRpcNotification> handle)
    {
        return new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                NotificationHandlers =
                [
                    new(method, (notification, cancellationToken) =>
                    {
                        handle(notification);

                        return default;
                    })
                ]
            }
        };
    }

    private static async Task<McpClient> ConnectAsync(
        string workspace,
        McpClientOptions? options = null,
        string? protocolVersion = null,
        CancellationToken cancellationToken = default)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerBinary.ServerPath(),
            Arguments = ["--workspace-root", workspace]
        });

        if (protocolVersion is not null)
        {
            options ??= new McpClientOptions();
            options.ProtocolVersion = protocolVersion;
        }

        return await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static T? Structured<T>(CallToolResult result)
    {
        return result.StructuredContent is null
            ? default
            : JsonSerializer.Deserialize<T>(result.StructuredContent.Value, McpJsonUtilities.DefaultOptions);
    }

    private static async Task<T> NextAsync<T>(ChannelReader<T> reader, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(NotificationTimeout);

        return await reader.ReadAsync(timeout.Token).ConfigureAwait(false);
    }

    private static async Task<string> ReadNotesAsync(string workspace, CancellationToken cancellationToken)
    {
        return await File.ReadAllTextAsync(
            Path.Combine(workspace, "notes", "study-notes.md"), cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTempWorkspace(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"probe-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return path;
    }

    private static void DeleteTempWorkspace(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string Render(bool? hint)
    {
        return hint?.ToString().ToLowerInvariant() ?? "-";
    }

    private static string Truncate(string text)
    {
        var firstLine = text.Split('\n')[0].TrimEnd();

        return firstLine.Length > 90 ? firstLine[..90] + "..." : firstLine;
    }
}
