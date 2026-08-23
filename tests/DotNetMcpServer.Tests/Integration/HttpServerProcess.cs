using System.Diagnostics;
using System.Text;
using DotNetMcpServer.Server.Hosting;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Launches the shipped server as a real subprocess serving Streamable HTTP, and waits until
/// Kestrel reports the port it bound.
/// </summary>
/// <remarks>
/// The process is asked for port 0 and tells the test which port it got, rather than the test
/// picking a number and hoping. Guessing a free port is a race that fails under a loaded CI
/// runner running several of these at once.
/// <para>
/// stdout is captured but never expected to receive anything. The rule that nothing in this
/// project writes to stdout holds for the HTTP host too, and
/// <see cref="StandardOutput"/> is what lets a test assert it.
/// </para>
/// </remarks>
internal sealed class HttpServerProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly StringBuilder _standardOutput = new();
    private readonly StringBuilder _standardError = new();
    private readonly Lock _gate = new();

    private HttpServerProcess(Process process)
    {
        _process = process;
    }

    /// <summary>The MCP endpoint, including the route the server mapped it to.</summary>
    /// <remarks>Assigned once the server reports the port it bound; never null after start.</remarks>
    public Uri Endpoint { get; private set; } = null!;

    /// <summary>Everything the server has written to stdout. Expected to stay empty.</summary>
    public string StandardOutput
    {
        get
        {
            lock (_gate)
            {
                return _standardOutput.ToString();
            }
        }
    }

    /// <summary>Everything the server has written to stderr, where all its logging goes.</summary>
    public string StandardError
    {
        get
        {
            lock (_gate)
            {
                return _standardError.ToString();
            }
        }
    }

    public static async Task<HttpServerProcess> StartAsync(string workspaceRoot, params string[] extraArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ServerLocator.ExecutablePath("DotNetMcpServer.Server"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--transport");
        startInfo.ArgumentList.Add("http");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");
        startInfo.ArgumentList.Add("--workspace-root");
        startInfo.ArgumentList.Add(workspaceRoot);

        foreach (var argument in extraArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The server process did not start.");

        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new HttpServerProcess(process);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (server._gate)
                {
                    server._standardOutput.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (server._gate)
            {
                server._standardError.AppendLine(e.Data);
            }

            var index = e.Data.IndexOf(HttpServerHost.ListeningMessagePrefix, StringComparison.Ordinal);
            if (index >= 0)
            {
                var address = e.Data[(index + HttpServerHost.ListeningMessagePrefix.Length)..].Trim();
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                {
                    listening.TrySetResult(uri);
                }
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            server.Endpoint = await listening.Task.WaitAsync(StartupTimeout);

            return server;
        }
        catch (TimeoutException)
        {
            Kill(process);
            throw new TimeoutException(
                $"The HTTP server did not report a listening address within {StartupTimeout.TotalSeconds:0}s. "
                + $"stderr was:{Environment.NewLine}{server.StandardError}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Kill(_process);
        await Task.CompletedTask;
        _process.Dispose();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process is already gone; nothing to stop.
        }
    }
}
