using System.Net.Http.Headers;
using System.Text;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Speaks Streamable HTTP by hand, for the things the SDK client deliberately hides.
/// </summary>
/// <remarks>
/// The official client is the right instrument for "does this server work", and the rest of
/// the integration suite uses it. These cases are about the transport itself — which status
/// code an expired session gets, whether an SSE event carries an <c>id</c>, what a
/// <c>Last-Event-ID</c> replays — and a client whose job is to paper over exactly those
/// details cannot observe them.
/// </remarks>
internal static class RawMcpHttp
{
    public const string SessionIdHeader = "Mcp-Session-Id";
    public const string ProtocolVersionHeader = "MCP-Protocol-Version";
    public const string LastEventIdHeader = "Last-Event-ID";

    /// <summary>
    /// The last revision that has both sessions and resumability. <c>2026-07-28</c> removed
    /// sessions (SEP-2567) and with them the standalone stream; <c>2025-06-18</c> has sessions
    /// but predates priming events, so its streams carry no event ids at all.
    /// </summary>
    public const string ResumableProtocol = "2025-11-25";

    /// <summary>A revision with sessions, used where resumability is not the subject.</summary>
    public const string SessionEraProtocol = "2025-06-18";

    /// <summary>One parsed <c>text/event-stream</c> event.</summary>
    public sealed record SseEvent(string EventType, string Data, string? Id);

    /// <summary>
    /// Completes the handshake and returns the session id the server issued, or null when it
    /// issued none — which is what stateless mode does.
    /// </summary>
    public static async Task<string?> InitializeAsync(HttpClient http, Uri endpoint, string protocolVersion)
    {
        string? sessionId;

        using (var response = await InitializeRawAsync(http, endpoint, protocolVersion))
        {
            response.EnsureSuccessStatusCode();
            sessionId = SessionIdOf(response);
        }

        using var initialized = await PostAsync(
            http,
            endpoint,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            sessionId,
            protocolVersion);

        return sessionId;
    }

    /// <summary>The raw <c>initialize</c> exchange, for cases that assert on its response.</summary>
    public static Task<HttpResponseMessage> InitializeRawAsync(
        HttpClient http,
        Uri endpoint,
        string protocolVersion)
    {
        // An ordinary escaped literal rather than a raw one: a JSON-RPC envelope ends in three
        // closing braces and a quote, which is precisely what raw string literals cannot
        // delimit without becoming less readable than the escapes they replace.
        var payload =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\""
            + protocolVersion
            + "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"interop-test\",\"version\":\"1.0\"}}}";

        return PostAsync(http, endpoint, payload, sessionId: null, protocolVersion);
    }

    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient http,
        Uri endpoint,
        string payload,
        string? sessionId,
        string protocolVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add(ProtocolVersionHeader, protocolVersion);

        if (sessionId is not null)
        {
            request.Headers.Add(SessionIdHeader, sessionId);
        }

        return await http.SendAsync(request);
    }

    /// <summary>
    /// Opens the standalone GET stream — the one an unsolicited server-to-client message
    /// arrives on, and the only stream the event store records.
    /// </summary>
    public static async Task<HttpResponseMessage> OpenEventStreamAsync(
        HttpClient http,
        Uri endpoint,
        string? sessionId,
        string? lastEventId,
        string protocolVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add(ProtocolVersionHeader, protocolVersion);

        if (sessionId is not null)
        {
            request.Headers.Add(SessionIdHeader, sessionId);
        }

        if (lastEventId is not null)
        {
            request.Headers.Add(LastEventIdHeader, lastEventId);
        }

        // Headers only: the body is an open stream that never completes on its own.
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Reads events until <paramref name="count"/> have arrived, the stream ends, or the token
    /// fires. Returns what it managed to read, so a caller can assert on a short read rather
    /// than only on a timeout.
    /// </summary>
    public static async Task<IReadOnlyList<SseEvent>> ReadEventsAsync(
        HttpResponseMessage response,
        int count,
        CancellationToken cancellationToken)
    {
        var events = new List<SseEvent>();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? eventType = null;
        string? data = null;
        string? id = null;

        try
        {
            while (events.Count < count)
            {
                var line = await reader.ReadLineAsync(cancellationToken);

                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    // A blank line ends one event. An event with no "event:" field is a
                    // comment or a keep-alive, and is not one of the ones being counted.
                    if (eventType is not null)
                    {
                        events.Add(new SseEvent(eventType, data ?? string.Empty, id));
                    }

                    eventType = null;
                    data = null;
                    id = null;

                    continue;
                }

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventType = line[6..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data = line[5..].Trim();
                }
                else if (line.StartsWith("id:", StringComparison.Ordinal))
                {
                    id = line[3..].Trim();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Whatever arrived before the deadline is the answer.
        }

        return events;
    }

    public static string? SessionIdOf(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(SessionIdHeader, out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
