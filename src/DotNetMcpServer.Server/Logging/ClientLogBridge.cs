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
/// Nothing is sent until the client asks. The bridge has no server to write to before then —
/// it learns which one from the <c>logging/setLevel</c> request itself, which is the earliest
/// point at which a client has expressed interest. Resolving <see cref="McpServer"/> from the
/// container instead would invert the dependency: the server is built from the logger factory
/// this provider belongs to.
/// <para>
/// Only this project's own log categories are mirrored. Forwarding the SDK's categories would
/// mean that sending a notification writes a log line that is itself sent, and that loop does
/// not terminate. stderr still receives everything either way.
/// </para>
/// </remarks>
public sealed class ClientLogBridge : ILoggerProvider
{
    private const string OwnCategoryPrefix = "DotNetMcpServer.";

    private readonly Lock _gate = new();

    private ILoggerProvider? _clientProvider;

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

    /// <summary>Binds the bridge to a session, if it is not bound already.</summary>
    public void Attach(McpServer? server)
    {
        if (server is null)
        {
            return;
        }

        lock (_gate)
        {
            _clientProvider ??= server.AsClientLoggerProvider();
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return categoryName.StartsWith(OwnCategoryPrefix, StringComparison.Ordinal)
            ? new DeferredLogger(this, categoryName)
            : NullLogger.Instance;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _clientProvider?.Dispose();
            _clientProvider = null;
        }
    }

    private ILoggerProvider? Provider()
    {
        lock (_gate)
        {
            return _clientProvider;
        }
    }

    /// <summary>
    /// A logger the factory can hand out before there is a session to write to. Loggers are
    /// created once, at first use of a category, which is usually long before the client has
    /// set a level.
    /// </summary>
    private sealed class DeferredLogger : ILogger
    {
        private readonly ClientLogBridge _bridge;
        private readonly string _category;

        private ILogger? _inner;

        public DeferredLogger(ClientLogBridge bridge, string category)
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
            return Resolve()?.IsEnabled(logLevel) == true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Resolve()?.Log(logLevel, eventId, state, exception, formatter);
        }

        private ILogger? Resolve()
        {
            return _inner ??= _bridge.Provider()?.CreateLogger(_category);
        }
    }
}
