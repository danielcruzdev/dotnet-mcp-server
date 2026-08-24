using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Mints bearer tokens for the authorization tests.
/// </summary>
/// <remarks>
/// The server validates signatures against a shared secret when one is configured, instead of
/// fetching an authority's JWKS. That exists so these tests can issue a token without standing
/// up an identity provider, and so a developer can try the authenticated path locally. What is
/// being tested is unaffected by it: issuer, audience, expiry and signature are checked the
/// same way whichever key source they came from. <c>F4-07</c> runs the same server against a
/// real provider.
/// <para>
/// The resource is a fixed logical identifier rather than the server's actual address. The
/// tests bind port 0, so the address is not known until after the process has started — and an
/// OAuth resource identifier is a name, not a route.
/// </para>
/// </remarks>
internal static class TestTokens
{
    /// <summary>Long enough for HMAC-SHA256, which refuses keys under 128 bits.</summary>
    public const string SigningKey = "dotnet-mcp-server-test-signing-key-not-a-secret";

    public const string Authority = "https://issuer.example.test";

    public const string Resource = "https://mcp.example.test/dotnet-mcp-server";

    public const string ToolsScope = "mcp:tools";

    public const string WriteScope = "mcp:write";

    /// <summary>The arguments that put a server behind this issuer.</summary>
    public static string[] ServerArguments(params string[] scopes)
    {
        var arguments = new List<string>
        {
            "--auth-authority", Authority,
            "--auth-signing-key", SigningKey,
            "--auth-resource", Resource
        };

        foreach (var scope in scopes)
        {
            arguments.Add("--auth-scope");
            arguments.Add(scope);
        }

        return [.. arguments];
    }

    public static string Issue(
        string? audience = null,
        string? issuer = null,
        IEnumerable<string>? scopes = null,
        TimeSpan? lifetime = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Authority,
            Audience = audience ?? Resource,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5)),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        if (scopes is not null)
        {
            // OAuth carries scopes as one space-delimited claim, which is what ASP.NET Core
            // splits when a policy asks for one.
            descriptor.Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["scope"] = string.Join(' ', scopes)
            };
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>A token that was valid and is not any more.</summary>
    public static string Expired()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = Resource,
            IssuedAt = DateTime.UtcNow.AddHours(-2),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
