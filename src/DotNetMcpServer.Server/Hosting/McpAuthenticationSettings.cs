namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// How this server behaves as an OAuth 2.1 protected resource.
/// </summary>
/// <remarks>
/// Authorization is off unless an authority is named. A server launched by Claude Desktop over
/// stdio, or run on loopback while developing, has no authorization server to check a token
/// against and no user to send through a consent screen — demanding one would make the default
/// configuration unusable rather than secure. Naming an authority is the operator saying this
/// instance is reachable by someone who needs to prove who they are.
/// </remarks>
internal sealed record McpAuthenticationSettings
{
    private const string AuthorityArgument = "--auth-authority";
    private const string SigningKeyArgument = "--auth-signing-key";
    private const string ResourceArgument = "--auth-resource";
    private const string ScopeArgument = "--auth-scope";

    private McpAuthenticationSettings(Uri authority, Uri resource, string? signingKey, IReadOnlyList<string> scopes)
    {
        Authority = authority;
        Resource = resource;
        SigningKey = signingKey;
        Scopes = scopes;
    }

    /// <summary>
    /// The authorization server that issues tokens for this resource. Doubles as the expected
    /// <c>iss</c> claim and, unless a signing key is supplied, as the JWKS source.
    /// </summary>
    public Uri Authority { get; }

    /// <summary>
    /// This server's canonical identifier: the RFC 9728 <c>resource</c>, and the audience a
    /// token must carry. A token minted for a different MCP server must not open this one, and
    /// the audience check is what enforces that.
    /// </summary>
    public Uri Resource { get; }

    /// <summary>
    /// A shared secret to validate signatures with, instead of fetching the authority's JWKS.
    /// </summary>
    /// <remarks>
    /// For development and for tests, which need to mint a token without standing up an
    /// identity provider first. Absent in any real deployment, where the authority's published
    /// keys are the only ones worth trusting.
    /// </remarks>
    public string? SigningKey { get; }

    /// <summary>Scopes advertised in the protected-resource metadata document.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>
    /// Reads the settings from the command line, then the environment. Returns null when no
    /// authority is configured, which leaves the endpoint open.
    /// </summary>
    public static McpAuthenticationSettings? Resolve(string[] args, string urls)
    {
        var authority = Read(args, AuthorityArgument, "MCP_AUTH_AUTHORITY");

        if (string.IsNullOrWhiteSpace(authority)
            || !Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri))
        {
            return null;
        }

        var resource = Read(args, ResourceArgument, "MCP_AUTH_RESOURCE");

        if (string.IsNullOrWhiteSpace(resource) || !Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri))
        {
            // The first configured URL is the best guess at what a client will call this
            // server. It is only a default: anything behind a proxy or a public hostname has to
            // say so with --auth-resource, because the audience check compares against whatever
            // the authorization server minted the token for.
            var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? HttpServerHost.DefaultUrls;

            resourceUri = new Uri(new Uri(first), HttpServerHost.EndpointPattern);
        }

        return new McpAuthenticationSettings(
            authorityUri,
            resourceUri,
            Read(args, SigningKeyArgument, "MCP_AUTH_SIGNING_KEY"),
            ReadAll(args, ScopeArgument, "MCP_AUTH_SCOPES"));
    }

    private static string? Read(string[] args, string argument, string environmentVariable)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(argument, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return Environment.GetEnvironmentVariable(environmentVariable);
    }

    private static List<string> ReadAll(string[] args, string argument, string environmentVariable)
    {
        var values = new List<string>();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(argument, StringComparison.OrdinalIgnoreCase))
            {
                values.Add(args[i + 1]);
            }
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            values.AddRange(fromEnvironment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return values;
    }
}
