namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// Decides which <c>Origin</c> headers this server will answer, as the MCP specification
/// requires of any Streamable HTTP server.
/// </summary>
/// <remarks>
/// The attack this exists to stop is DNS rebinding. A local MCP server is reachable at
/// <c>127.0.0.1</c>, and a page the user happens to have open can be made to resolve its own
/// hostname there — at which point the browser will happily send it requests carrying the
/// user's ambient authority over their own machine. What the page cannot do is forge the
/// <c>Origin</c> header, so refusing origins the operator did not name is the defence.
/// <para>
/// A request with no <c>Origin</c> at all is allowed. The header is something browsers attach;
/// the clients this server is built for — Claude Desktop, VS Code, a console agent — are not
/// browsers and send nothing. Rejecting an absent header would refuse every real client to
/// defend against an attacker who is not using a browser and could therefore send any origin
/// they liked. The literal string <c>null</c> is a different matter: that is a real origin, the
/// one a sandboxed iframe sends, and it is refused.
/// </para>
/// </remarks>
internal sealed class OriginPolicy
{
    private const string Argument = "--allowed-origin";
    private const string EnvironmentVariable = "MCP_ALLOWED_ORIGINS";

    private readonly HashSet<string> _allowed;

    private OriginPolicy(IEnumerable<string> allowed)
    {
        // Origins are compared case-insensitively on scheme and host, which is what
        // OrdinalIgnoreCase gives for the normalised form produced by Normalize.
        _allowed = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Origins named explicitly by the operator, in their normalised form.</summary>
    public IReadOnlyCollection<string> Allowed => _allowed;

    /// <summary>
    /// Reads the allowlist from repeated <c>--allowed-origin</c> arguments, then from
    /// <c>MCP_ALLOWED_ORIGINS</c> as a comma-separated list.
    /// </summary>
    public static OriginPolicy Resolve(string[] args)
    {
        var allowed = new List<string>();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(Argument, StringComparison.OrdinalIgnoreCase))
            {
                AddNormalized(allowed, args[i + 1]);
            }
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            foreach (var candidate in fromEnvironment.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                AddNormalized(allowed, candidate);
            }
        }

        return new OriginPolicy(allowed);
    }

    /// <summary>Whether a request carrying this <c>Origin</c> should be answered.</summary>
    /// <param name="origin">The raw header value, or null when the request carried none.</param>
    public bool IsAllowed(string? origin)
    {
        if (origin is null)
        {
            return true;
        }

        var normalized = Normalize(origin);

        if (normalized is null)
        {
            // Unparseable, or the literal "null" an opaque origin sends. Neither is something
            // an operator can have meant to allow.
            return false;
        }

        if (_allowed.Contains(normalized))
        {
            return true;
        }

        // Loopback is allowed without configuration so that a page served from the developer's
        // own machine works out of the box. It gives a rebinding attacker nothing: the whole
        // point of the attack is that the page keeps its own origin, and that origin is the
        // attacker's domain rather than localhost.
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    private static void AddNormalized(List<string> allowed, string candidate)
    {
        var normalized = Normalize(candidate);

        if (normalized is not null)
        {
            allowed.Add(normalized);
        }
    }

    /// <summary>
    /// Reduces an origin to scheme, host and port, so that <c>http://localhost:3000</c> and
    /// <c>http://localhost:3000/</c> are one value rather than two.
    /// </summary>
    private static string? Normalize(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0 || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
    }
}
