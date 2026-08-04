using DotNetMcpServer.Server.Workspace;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Resources;

/// <summary>
/// Turns filesystem activity into <c>notifications/resources/updated</c> and
/// <c>notifications/resources/list_changed</c>, for the resources a client asked to follow.
/// </summary>
/// <remarks>
/// The watcher starts on the first request that shows interest in resources — a
/// <c>resources/list</c> or a <c>resources/subscribe</c> — rather than at startup, so a
/// session that only calls tools never watches the filesystem at all.
/// <para>
/// Events are debounced per URI. A single save produces several
/// <see cref="FileSystemWatcher"/> events on every platform this runs on, and a client that
/// re-reads a document on each of them does three reads of the same bytes. The timer is
/// trailing-edge, so what the client is told about is the state after the writer stopped.
/// </para>
/// <para>
/// <b>Protocol boundary.</b> <see cref="Subscribe"/> is driven by <c>resources/subscribe</c>,
/// which the <c>2026-07-28</c> revision removed in favour of <c>subscriptions/listen</c>
/// (SEP-2575). On that revision the SDK keeps the subscription list itself and exposes no
/// server-side hook for it, so per-resource updates reach clients negotiating
/// <c>2025-11-25</c> or earlier — which is every shipping client today — and list-changed
/// notifications reach all of them. The decision log records why this was left as it is.
/// </para>
/// </remarks>
public sealed partial class WorkspaceResourceSubscriptions : IDisposable
{
    /// <summary>Quiet period a path must observe before its notification is sent.</summary>
    private const int DebounceMilliseconds = 250;

    /// <summary>Debounce key for the one notification that is not about a single URI.</summary>
    private const string ListChangedKey = "\0list-changed";

    private readonly WorkspaceContext _workspace;
    private readonly ILogger<WorkspaceResourceSubscriptions> _logger;

    private readonly Lock _gate = new();
    private readonly HashSet<string> _subscribedUris = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Timer> _pending = new(StringComparer.Ordinal);

    private FileSystemWatcher? _watcher;
    private McpServer? _server;
    private bool _disposed;

    public WorkspaceResourceSubscriptions(
        WorkspaceContext workspace,
        ILogger<WorkspaceResourceSubscriptions> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>Begins watching the workspace, if it is not being watched already.</summary>
    public void EnsureWatching(McpServer? server)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _server ??= server;

            if (_watcher is not null || _server is null)
            {
                return;
            }

            _watcher = new FileSystemWatcher(_workspace.Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _watcher.Created += OnEntryAddedOrRemoved;
            _watcher.Deleted += OnEntryAddedOrRemoved;
            _watcher.Renamed += OnEntryRenamed;
            _watcher.Changed += OnEntryChanged;
            _watcher.EnableRaisingEvents = true;

            LogWatching(_workspace.Root);
        }
    }

    /// <summary>Records a subscription to a resource URI in this server's scheme.</summary>
    /// <exception cref="McpProtocolException">The URI is not one this server can serve.</exception>
    public void Subscribe(McpServer? server, string uri)
    {
        RequireOwnUri(uri);
        EnsureWatching(server);

        lock (_gate)
        {
            _subscribedUris.Add(uri);
        }

        LogSubscribed(uri);
    }

    /// <summary>
    /// Drops a subscription. Unsubscribing from something never subscribed to is not an
    /// error — the client's intent is satisfied either way.
    /// </summary>
    /// <exception cref="McpProtocolException">The URI is not one this server can serve.</exception>
    public void Unsubscribe(string uri)
    {
        RequireOwnUri(uri);

        lock (_gate)
        {
            _subscribedUris.Remove(uri);
        }

        LogUnsubscribed(uri);
    }

    /// <summary>Whether a URI currently has a subscriber. Exposed for tests.</summary>
    internal bool IsSubscribed(string uri)
    {
        lock (_gate)
        {
            return _subscribedUris.Contains(uri);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;

            foreach (var timer in _pending.Values)
            {
                timer.Dispose();
            }

            _pending.Clear();
        }
    }

    private static void RequireOwnUri(string uri)
    {
        if (!WorkspaceResourceProvider.TryGetRelativePath(uri, out _))
        {
            throw new McpProtocolException(
                $"Not a resource of this server: {uri}. Expected a {WorkspaceResourceProvider.UriPrefix}… URI.",
                McpErrorCode.InvalidParams);
        }
    }

    private void OnEntryChanged(object sender, FileSystemEventArgs e)
    {
        Touch(e.Name, listChanged: false);
    }

    private void OnEntryAddedOrRemoved(object sender, FileSystemEventArgs e)
    {
        Touch(e.Name, listChanged: true);
    }

    private void OnEntryRenamed(object sender, RenamedEventArgs e)
    {
        // Both ends matter: the old name left the list, the new one joined it.
        Touch(e.OldName, listChanged: true);
        Touch(e.Name, listChanged: true);
    }

    /// <summary>
    /// Handles one watcher event. The name is taken from the event rather than derived from
    /// <see cref="FileSystemEventArgs.FullPath"/> against the root: the watcher already
    /// reports it relative to the directory it watches, which sidesteps comparing a
    /// configured root against a path the OS may have resolved differently — a symlinked
    /// temp directory on macOS, an 8.3 path on Windows.
    /// </summary>
    private void Touch(string? name, bool listChanged)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var relativePath = name.Replace('\\', '/');

        if (!WorkspaceResourceProvider.IsDocument(relativePath))
        {
            return;
        }

        if (listChanged)
        {
            Schedule(ListChangedKey, SendListChangedAsync);
        }

        var uri = WorkspaceResourceProvider.ToUri(relativePath);

        lock (_gate)
        {
            if (!_subscribedUris.Contains(uri))
            {
                return;
            }
        }

        Schedule(uri, () => SendUpdatedAsync(uri));
    }

    private void Schedule(string key, Func<Task> send)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_pending.TryGetValue(key, out var timer))
            {
                timer = new Timer(_ => Fire(key, send));
                _pending[key] = timer;
            }

            timer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void Fire(string key, Func<Task> send)
    {
        lock (_gate)
        {
            if (_pending.Remove(key, out var timer))
            {
                timer.Dispose();
            }

            if (_disposed)
            {
                return;
            }
        }

        _ = SendAsync(send);
    }

    private async Task SendAsync(Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The session can end between a file changing and the notification being written.
            // That is not a server fault and must not take the process down from a timer
            // thread, so it is logged and dropped.
            LogNotificationFailed(exception);
        }
    }

    private Task SendUpdatedAsync(string uri)
    {
        var server = Server();

        return server is null
            ? Task.CompletedTask
            : server.SendNotificationAsync(
                NotificationMethods.ResourceUpdatedNotification,
                new ResourceUpdatedNotificationParams { Uri = uri });
    }

    private Task SendListChangedAsync()
    {
        var server = Server();

        return server is null
            ? Task.CompletedTask
            : server.SendNotificationAsync(
                NotificationMethods.ResourceListChangedNotification,
                new ResourceListChangedNotificationParams());
    }

    private McpServer? Server()
    {
        lock (_gate)
        {
            return _disposed ? null : _server;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Watching {WorkspaceRoot} for resource changes")]
    private partial void LogWatching(string workspaceRoot);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client subscribed to {Uri}")]
    private partial void LogSubscribed(string uri);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client unsubscribed from {Uri}")]
    private partial void LogUnsubscribed(string uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A resource notification could not be delivered")]
    private partial void LogNotificationFailed(Exception exception);
}
