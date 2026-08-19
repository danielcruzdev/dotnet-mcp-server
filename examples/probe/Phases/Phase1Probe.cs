using System.Text.Json;
using DotNetMcpServer.Server.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetMcpServer.Probe.Phases;

/// <summary>
/// Phase 1 — the migration onto the official SDK. What it delivered is the thing that was
/// broken before it: a server a client which is not this repository can actually talk to.
/// </summary>
/// <remarks>
/// The hand-written artifact is Phase 1's other half and is not probed here. It is driven by
/// <c>HandwrittenServerInteropTests</c>, which carries <c>[Trait("Phase", "1")]</c>, so
/// <c>probe phase1 --tests</c> runs it.
/// </remarks>
internal static class Phase1Probe
{
    /// <summary>A document that exists in <c>examples/workspace/</c>.</summary>
    private const string SampleDocument = "dotnet-concepts.md";

    public static IReadOnlyList<string> Capabilities { get; } = ["handshake", "tools", "call", "containment"];

    public static async Task RunAsync(Report report, string? capability, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> selected = capability is null ? Capabilities : [capability];

        await using var client = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        foreach (var name in selected)
        {
            report.Section($"Phase 1 - {name}");

            switch (name)
            {
                case "handshake":
                    Handshake(report, client);
                    break;

                case "tools":
                    await ToolsAsync(report, client, cancellationToken).ConfigureAwait(false);
                    break;

                case "call":
                    await CallAsync(report, client, cancellationToken).ConfigureAwait(false);
                    break;

                case "containment":
                    await ContainmentAsync(report, client, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    report.Fail($"'{name}' is not a Phase 1 capability.");
                    break;
            }
        }
    }

    /// <summary>
    /// Reaching this point at all is the check: the handshake completed over newline-delimited
    /// JSON, which is what finding C1 was about.
    /// </summary>
    private static void Handshake(Report report, McpClient client)
    {
        report.Value("serverInfo.name", client.ServerInfo.Name);
        report.Value("serverInfo.version", client.ServerInfo.Version ?? "(none)");
        report.Value("binary", ServerBinary.ServerPath());
        report.Value("workspace", ServerBinary.ExampleWorkspace);

        report.Check(
            !string.IsNullOrWhiteSpace(client.ServerInfo.Name),
            "initialize completed and the server identified itself");
    }

    private static async Task ToolsAsync(Report report, McpClient client, CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("tools/list", $"{tools.Count} tools");

        foreach (var tool in tools.OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            report.Item($"{tool.Name,-22}{tool.Description}");
        }

        report.Check(tools.Count > 0, "the server advertises its tools");
        report.Check(
            tools.All(tool => !string.IsNullOrWhiteSpace(tool.Description)),
            "every tool describes itself well enough for a model to choose it");
    }

    private static async Task CallAsync(Report report, McpClient client, CancellationToken cancellationToken)
    {
        var calculation = await CallAsync<CalculationResult>(
            client,
            "calculate_expression",
            new Dictionary<string, object?> { ["expression"] = "(1200 + 350) / 5" },
            cancellationToken).ConfigureAwait(false);

        // Reading the properties is what couples this file to the server at compile time.
        report.Value("calculate_expression", $"{calculation?.Expression} = {calculation?.Result}");
        report.Check(calculation?.Result == 310m, "an expression is evaluated and returned as data");

        var now = await CallAsync<CurrentDateTime>(
            client,
            "get_current_datetime",
            // The argument is 'timezone'; the property it comes back on is 'timeZone'. Getting
            // that wrong is silent — an unknown argument leaves the optional parameter at its
            // default and the answer arrives in UTC.
            new Dictionary<string, object?> { ["timezone"] = "America/Sao_Paulo" },
            cancellationToken).ConfigureAwait(false);

        report.Value("get_current_datetime", $"{now?.Iso8601}  ({now?.Formatted})");
        report.Check(
            now?.TimeZone == "America/Sao_Paulo",
            "an IANA timezone resolves — which is why InvariantGlobalization stays false");

        var read = await client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = SampleDocument, ["maxCharacters"] = 240 },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("read_text_file", SampleDocument);

        foreach (var line in TextOf(read).Split('\n').Take(6))
        {
            report.Item(line.TrimEnd());
        }

        report.Check(read.IsError != true, "a workspace document is read back through the protocol");
    }

    private static async Task ContainmentAsync(Report report, McpClient client, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(
            "read_text_file",
            new Dictionary<string, object?> { ["path"] = "../../../etc/passwd" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        report.Value("read_text_file", "../../../etc/passwd");
        report.Value("isError", (result.IsError == true).ToString());
        report.Item(TextOf(result));

        report.Check(result.IsError == true, "a path escaping the workspace is refused");
    }

    private static async Task<T?> CallAsync<T>(
        McpClient client,
        string tool,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true || result.StructuredContent is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(result.StructuredContent.Value, McpJsonUtilities.DefaultOptions);
    }

    private static string TextOf(CallToolResult result)
    {
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    private static async Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "dotnet-mcp-server",
            Command = ServerBinary.ServerPath(),
            Arguments = ["--workspace-root", ServerBinary.ExampleWorkspace]
        });

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
