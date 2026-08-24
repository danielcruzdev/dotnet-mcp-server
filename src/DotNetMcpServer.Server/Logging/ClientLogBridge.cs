using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Logging;

// MCP9005: the whole logging feature is deprecated by specification revision 2026-07-28
// (SEP-2577), so every SDK member this file touches is marked obsolete. The suppression is
// file-scoped rather than per-line because the type exists for that one feature: when the SDK
// removes it, this file goes with it. Until then the capability is live for every client
// negotiating 2025-11-25 or earlier — and the SDK advertises it either way, so a server that
// ignored it would be advertising something it never delivers. See the decision log.
#pragma warning disable MCP9005

/// <summary>
/// Mirrors this server's own <see cref="ILogger"/> output to the client as
/// <c>notifications/message</c>, at the level the client asked for with
/// <c>logging/setLevel</c>.
/// </summary>
/// <remarks>
/// Nothing is sent until a client asks. The bridge has no server to write to before then — it
/// learns which one from the <c>logging/setLevel</c> request itself, which is the earliest
/// point at which a client has expressed interest. Resolving <see cref="McpServer"/> from the
/// container instead would invert the dependency: the server is built from the logger factory
/// this provider belongs to.
/// <para>
/// One process can serve many sessions at once over HTTP, so the bridge keeps one client
/// provider <em>per session</em> and picks between them by the <c>Mcp-Session-Id</c> of the
/// request being handled. A single shared provider would send one client's log messages to
/// another client, which is considerably worse than sending none. Over stdio there is exactly
/// one session and no <see cref="IHttpContextAccessor"/>, so every lookup lands on one entry.
/// </para>
/// <para>
/// Only this project's own log categories are mirrored. Forwarding the SDK's categories would
/// mean that sending a notification writes a log line that is itself sent, and that loop does
/// not terminate. stderr still receives everything either way.
/// </para>
/// </remarks>
public sealed class ClientLogBridge : ILoggerProvider
{
    private const string OwnCategoryPrefix = "DotNetMcpServer.";

    /// <summary>
    /// The key used when there is no session id: stdio, and HTTP in stateless mode. Both have
    /// exactly one client to write to at a time.
    /// </summary>
    private const string SingleSessionKey = "";

    /// <summary>
    /// Spelled out rather than taken from the SDK: <c>McpHttpHeaders</c> is internal to
    /// ModelContextProtocol.Core. Removed from the protocol by the 2026-07-28 revision
    /// (SEP-2567), so it is only ever present on a stateful session.
    /// </summary>
    private const string SessionIdHeader = "Mcp-Session-Id";

    private readonly ConcurrentDictionary<string, ILoggerProvider> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Session, string Category), ILogger> _loggers = new();
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <param name="httpContextAccessor">
    /// Supplies the session id of the request being handled. Null on the stdio host, where
    /// there is only ever one session.
    /// </param>
    public ClientLogBridge(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// The <c>logging/setLevel</c> handler. The SDK has already recorded the requested level
    /// on the server by the time this runs; what it cannot do is tell this provider which
    /// session to write to.
    /// </summary>
    public static ValueTask<EmptyResult> AttachOnSetLevelAsync(
        RequestContext<SetLevelRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = request.Services
            ?? throw new InvalidOperationException("The request has no service provider.");

        services.GetRequiredService<ClientLogBridge>().Attach(request.Server);

        return ValueTask.FromResult(new EmptyResult());
    }

    /// <summary>Binds the bridge to a session, if that session is not bound already.</summary>
    public void Attach(McpServer? server)
    {
        if (server is null)
        {
            return;
        }

        _sessions.GetOrAdd(Key(server.SessionId), _ => server.AsClientLoggerProvider());
    }

    /// <summary>
    /// Releases a session's provider once that session ends, so a long-running HTTP host does
    /// not accumulate one provider for every client it has ever served.
    /// </summary>
    public void Detach(string? sessionId)
    {
        var key = Key(sessionId);

        if (_sessions.TryRemove(key, out var provider))
        {
            provider.Dispose();
        }

        foreach (var entry in _loggers.Keys)
        {
            if (string.Equals(entry.Session, key, StringComparison.Ordinal))
            {
                _loggers.TryRemove(entry, out _);
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return categoryName.StartsWith(OwnCategoryPrefix, StringComparison.Ordinal)
            ? new SessionRoutedLogger(this, categoryName)
            : NullLogger.Instance;
    }

    public void Dispose()
    {
        foreach (var key in _sessions.Keys)
        {
            if (_sessions.TryRemove(key, out var provider))
            {
                provider.Dispose();
            }
        }

        _loggers.Clear();
    }

    private static string Key(string? sessionId)
    {
        return string.IsNullOrEmpty(sessionId) ? SingleSessionKey : sessionId;
    }

    /// <summary>
    /// The session the request being handled belongs to, or the single-session key when the
    /// host has no notion of concurrent sessions.
    /// </summary>
    private string CurrentSessionKey()
    {
        var context = _httpContextAccessor?.HttpContext;

        if (context is null)
        {
            return SingleSessionKey;
        }

        return Key(context.Request.Headers[SessionIdHeader].FirstOrDefault());
    }

    private ILogger? CurrentLogger(string category)
    {
        var session = CurrentSessionKey();

        // A miss is deliberately not cached: a category is first logged long before the client
        // sets a level, and remembering the miss would silence that session permanently.
        return _sessions.TryGetValue(session, out var provider)
            ? _loggers.GetOrAdd((session, category), key => provider.CreateLogger(key.Category))
            : null;
    }

    /// <summary>
    /// A logger the factory can hand out before there is a session to write to. Loggers are
    /// created once, at first use of a category, which is usually long before any client has
    /// set a level — and, on the HTTP host, before the session it belongs to even exists.
    /// </summary>
    private sealed class SessionRoutedLogger : ILogger
    {
        private readonly ClientLogBridge _bridge;
        private readonly string _category;

        public SessionRoutedLogger(ClientLogBridge bridge, string category)
        {
            _bridge = bridge;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _bridge.CurrentLogger(_category)?.IsEnabled(logLevel) == true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _bridge.CurrentLogger(_category)?.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
