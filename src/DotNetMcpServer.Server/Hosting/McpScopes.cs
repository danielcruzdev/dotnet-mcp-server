using System.Security.Claims;

namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// The OAuth scopes this server's tools are divided by, and the authorization policies of the
/// same names.
/// </summary>
/// <remarks>
/// The split is by what a tool does to the user's disk rather than by tool count: everything
/// that only reads sits behind <see cref="Tools"/>, and the one tool that writes needs
/// <see cref="Write"/> as well. A client that only ever answers questions can therefore be
/// issued a token that cannot modify anything, which is the entire point of having scopes
/// rather than a single "may call this server" bit.
/// <para>
/// These names are also what the server advertises in its protected-resource metadata, so an
/// authorization server and its consent screen can offer exactly these two.
/// </para>
/// </remarks>
public static class McpScopes
{
    /// <summary>Permits the read-only tools.</summary>
    public const string Tools = "mcp:tools";

    /// <summary>Permits the tools that write to the workspace.</summary>
    public const string Write = "mcp:write";

    /// <summary>
    /// Whether a principal carries a scope.
    /// </summary>
    /// <remarks>
    /// OAuth puts every granted scope in one space-delimited claim, so the claim value is a
    /// list rather than a single scope and a plain claim comparison would only ever match a
    /// token that had been granted exactly one thing. Both spellings are read: <c>scope</c> is
    /// the RFC 9068 name, <c>scp</c> is what Microsoft Entra issues.
    /// </remarks>
    public static bool HasScope(ClaimsPrincipal user, string scope)
    {
        foreach (var claim in user.FindAll("scope").Concat(user.FindAll("scp")))
        {
            foreach (var granted in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(granted, scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
