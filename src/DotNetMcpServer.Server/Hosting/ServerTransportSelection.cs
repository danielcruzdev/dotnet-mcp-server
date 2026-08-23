namespace DotNetMcpServer.Server.Hosting;

/// <summary>The transport this process serves MCP over.</summary>
internal enum ServerTransport
{
    /// <summary>Newline-delimited JSON over stdin/stdout. The default, and what local clients launch.</summary>
    Stdio,

    /// <summary>Streamable HTTP. What a remote client connects to.</summary>
    Http
}

/// <summary>
/// Reads the requested transport from the command line, then the environment.
/// </summary>
/// <remarks>
/// stdio is the default because every client configuration written before this existed
/// launches the binary with no transport argument, and those must keep working.
/// </remarks>
internal static class ServerTransportSelection
{
    private const string Argument = "--transport";
    private const string EnvironmentVariable = "MCP_TRANSPORT";

    public static ServerTransport Resolve(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(Argument, StringComparison.OrdinalIgnoreCase))
            {
                return Parse(args[i + 1]);
            }
        }

        return Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));
    }

    private static ServerTransport Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "http" or "streamable-http" => ServerTransport.Http,
            _ => ServerTransport.Stdio
        };
    }
}
