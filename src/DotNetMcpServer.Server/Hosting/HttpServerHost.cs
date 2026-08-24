using System.Text;
using DotNetMcpServer.Server.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Server;

namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// The remote transport: MCP over Streamable HTTP, served by Kestrel.
/// </summary>
/// <remarks>
/// The MCP capability surface is registered by
/// <see cref="McpServerRegistration.AddWorkspaceMcpServer"/>, exactly as it is for stdio — the
/// only difference between the two hosts is the transport and the things a network listener
/// needs that a subprocess does not.
/// </remarks>
internal static partial class HttpServerHost
{
    /// <summary>The route the MCP endpoint is mapped to.</summary>
    internal const string EndpointPattern = "/mcp";

    /// <summary>Opts into the deprecated stateful Streamable HTTP mode.</summary>
    internal const string SessionsArgument = "--http-sessions";

    private const string SessionsEnvironmentVariable = "MCP_HTTP_SESSIONS";

    /// <summary>
    /// Loopback by default. A remote MCP server that binds every interface the moment it
    /// starts is how a local tool server becomes a public one by accident — the spec calls out
    /// localhost binding for exactly this reason.
    /// </summary>
    internal const string DefaultUrls = "http://127.0.0.1:3001";

    /// <summary>
    /// Written to stderr once Kestrel has bound, so a caller that asked for port 0 can learn
    /// which port it got. stdout stays untouched in this host too: the invariant that nothing
    /// in this project writes to stdout is worth more than the convenience of breaking it
    /// where it happens to be safe.
    /// </summary>
    internal const string ListeningMessagePrefix = "MCP Streamable HTTP listening on ";

    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // CreateBuilder has already added a console provider writing to stdout. Replacing it
        // rather than adding to it keeps this host to a single provider, pinned to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        // Respects --urls and ASPNETCORE_URLS when either is set; supplies loopback when
        // neither is.
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
        {
            builder.Configuration["urls"] = DefaultUrls;
        }

        // The bridge picks a client to write to by the Mcp-Session-Id of the request being
        // handled, which is what makes it safe with several sessions in flight at once.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(services => new ClientLogBridge(
            services.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddSingleton<ILoggerProvider>(
            services => services.GetRequiredService<ClientLogBridge>());

        // The client, not this configuration, decides how verbose the log stream it receives
        // is, so every level has to reach the bridge for logging/setLevel to have anything to
        // filter. The console keeps its own default and is unaffected.
        builder.Logging.AddFilter<ClientLogBridge>(category: null, level: LogLevel.Trace);

        var sessionsEnabled = HasFlag(args, SessionsArgument, SessionsEnvironmentVariable);

        var mcpServer = builder.Services
            .AddWorkspaceMcpServer(args)
            .WithHttpTransport(options =>
            {
                options.Stateless = !sessionsEnabled;

                if (!sessionsEnabled)
                {
                    return;
                }

                // MCP9006: stateful Streamable HTTP is deprecated, and with it these two knobs.
                // Suppressed narrowly rather than repository-wide: they are only reachable on
                // the opt-in path, and when the SDK deletes them the compiler should point at
                // exactly the branch that has to go. See the decision log.
#pragma warning disable MCP9006
                // A session parked with no request in flight is a client that walked away. Two
                // hours is the SDK default, sized for a desktop client holding a conversation
                // open; twenty minutes covers that and returns an abandoned session's memory
                // considerably sooner.
                options.IdleTimeout = TimeSpan.FromMinutes(20);

                // Bounded so a client that opens sessions and never closes them cannot grow the
                // process without limit.
                options.MaxIdleSessionCount = 128;

#pragma warning restore MCP9006
            });

        if (sessionsEnabled)
        {
            // Resumability. The SDK generates an id for every SSE event, retains them, and
            // replays whatever a reconnecting client missed when it presents Last-Event-ID.
            // The backing store is an IDistributedCache, so the in-memory one here is the
            // single-process choice and swapping in Redis is a registration change rather
            // than a code change.
            //
            // Inside this branch because resumability is part of the same escape hatch: the
            // store carries MCP9006 for SEP-2567 exactly as the session knobs above do. There
            // is no Last-Event-ID to honour without a session to replay it onto.
            builder.Services.AddDistributedMemoryCache();
#pragma warning disable MCP9006
            mcpServer.WithDistributedCacheEventStreamStore();
#pragma warning restore MCP9006

            // Releases the session's client logger when its session ends; without this the
            // bridge would hold one provider for every client the process has ever served.
            //
            // Configured through the options pipeline rather than inside WithHttpTransport so
            // the bridge comes from the root container. The obvious spelling — reading it off
            // the HttpContext the handler is handed — throws ObjectDisposedException: that
            // context belongs to the request that *started* the session, which completed long
            // before the session ends.
            //
            // MCPEXP002: RunSessionHandler is an evaluation-only API. It is the only hook the
            // SDK offers for "this session is over", and it is used here to free memory rather
            // than to alter protocol behaviour, so if it disappears the fallback is a bounded
            // leak rather than a broken server.
            builder.Services
                .AddOptions<HttpServerTransportOptions>()
                .Configure<ClientLogBridge>((options, bridge) =>
#pragma warning disable MCPEXP002
                    options.RunSessionHandler = async (httpContext, server, cancellationToken) =>
                    {
                        try
                        {
                            await server.RunAsync(cancellationToken);
                        }
                        finally
                        {
                            bridge.Detach(server.SessionId);
                        }
                    });
#pragma warning restore MCPEXP002
        }

        // Registered on its own so the suppression covers this one call and nothing else.
#pragma warning disable MCP9005 // Logging is deprecated by 2026-07-28 (SEP-2577); see ClientLogBridge.
        mcpServer.WithSetLoggingLevelHandler(ClientLogBridge.AttachOnSetLevelAsync);
#pragma warning restore MCP9005

        // OAuth 2.1 resource server. Off unless an authority is named; see
        // McpAuthenticationSettings for why that is the default rather than a lapse.
        var authentication = McpAuthenticationSettings.Resolve(args, builder.Configuration["urls"]!);

        // Honours [Authorize] on tools, prompts and resources. Registered unconditionally,
        // because the SDK fails closed: finding authorization metadata on a tool with no
        // filter to evaluate it is an error rather than a licence to serve the tool anyway.
        // That is the right call, and it is why the policies below have to exist even when
        // this server is not protecting anything.
        mcpServer.AddAuthorizationFilters();
        AddToolScopePolicies(builder, enforced: authentication is not null);

        if (authentication is not null)
        {
            AddResourceServer(builder, authentication);
        }

        var app = builder.Build();

        // Origin validation runs ahead of the MCP endpoint so a rebinding attempt is refused
        // before it reaches any protocol handling. See OriginPolicy for what it defends.
        var origins = OriginPolicy.Resolve(args);

        // Anonymous on purpose, and separate from the MCP endpoint: a container orchestrator
        // has no token, and asking it to hold one to find out whether the process is alive
        // would make the probe the most privileged thing in the deployment.
        app.MapGet(HealthCheckCommand.Path, () => Results.Ok("healthy")).AllowAnonymous();

        var mcpEndpoint = app.MapMcp(EndpointPattern);

        if (authentication is not null)
        {
            mcpEndpoint.RequireAuthorization();
        }

        mcpEndpoint.AddEndpointFilter(async (context, next) =>
        {
            var origin = context.HttpContext.Request.Headers.Origin.FirstOrDefault();

            if (!origins.IsAllowed(origin))
            {
                LogOriginRejected(
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(HttpServerHost).FullName!),
                    origin ?? string.Empty);

                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });

        await app.StartAsync();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HttpServerHost).FullName!);
        foreach (var address in BoundAddresses(app))
        {
            LogListening(logger, address + EndpointPattern);
        }

        await app.WaitForShutdownAsync();
    }

    /// <summary>
    /// Reads a boolean switch from the command line, then the environment. Present with no
    /// value counts as enabled, which is how a flag is normally written.
    /// </summary>
    private static bool HasFlag(string[] args, string argument, string environmentVariable)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(argument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var next = i + 1 < args.Length ? args[i + 1] : null;

            return next is null
                || next.StartsWith("--", StringComparison.Ordinal)
                || bool.TryParse(next, out var parsed) && parsed;
        }

        return bool.TryParse(Environment.GetEnvironmentVariable(environmentVariable), out var fromEnvironment)
            && fromEnvironment;
    }

    /// <summary>
    /// Registers bearer-token validation and the RFC 9728 metadata document that tells an
    /// unauthenticated client where to go and get a token.
    /// </summary>
    private static void AddResourceServer(WebApplicationBuilder builder, McpAuthenticationSettings settings)
    {
        builder.Services
            .AddAuthentication(options =>
            {
                // The two schemes do different jobs. JWT bearer decides whether a presented
                // token is good; the MCP scheme owns the refusal, because a 401 has to carry a
                // WWW-Authenticate pointing at the metadata document or the client has no way
                // to discover which authorization server to use.
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Authority.ToString().TrimEnd('/'),
                    ValidateLifetime = true,

                    // The audience check is what stops a token minted for some other MCP server
                    // from opening this one. RFC 8707 calls it resource indicators; here it is
                    // simply the claim this server refuses to ignore.
                    ValidateAudience = true,
                    ValidAudience = settings.Resource.ToString(),

                    ValidateIssuerSigningKey = true
                };

                if (settings.SigningKey is null)
                {
                    // Production: trust only what the authority publishes.
                    options.Authority = settings.Authority.ToString();

                    if (settings.MetadataAddress is not null)
                    {
                        // Fetch the keys over a name this process can resolve, while still
                        // requiring the issuer the tokens actually carry. See
                        // McpAuthenticationSettings.MetadataAddress.
                        options.MetadataAddress = settings.MetadataAddress;
                        options.RequireHttpsMetadata = false;
                    }
                }
                else
                {
                    // Development and tests: a shared secret, so a token can be minted without
                    // an identity provider to stand up first.
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
                    options.RequireHttpsMetadata = false;
                }
            })
            .AddMcp(options => options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = settings.Resource.ToString(),
                AuthorizationServers = { settings.Authority.ToString() },
                ScopesSupported = [.. settings.Scopes]

                // BearerMethodsSupported is left alone: it already contains "header", and a
                // collection initializer appends rather than replaces, so naming it again
                // published it twice.
            });

    }

    /// <summary>
    /// Registers one authorization policy per scope, so a tool can name the permission it
    /// needs rather than merely requiring that somebody is signed in.
    /// </summary>
    /// <param name="enforced">
    /// Whether the policies actually check anything. False on a server with no authorization
    /// server configured, where they pass for everyone.
    /// </param>
    /// <remarks>
    /// The trivially-satisfied variant is not a hole that has been left open; it is the same
    /// gate stated once. A server that was never told about an authorization server has no
    /// tokens to inspect and no issuer to check them against, so the only choices are to serve
    /// its tools to the caller in front of it — which is what an unprotected server means — or
    /// to serve nothing at all. Naming an authority is what turns the scopes on, and it is the
    /// one decision an operator has to make.
    /// </remarks>
    private static void AddToolScopePolicies(WebApplicationBuilder builder, bool enforced)
    {
        builder.Services
            .AddAuthorizationBuilder()
            .AddPolicy(McpScopes.Tools, policy => policy.RequireAssertion(
                context => !enforced || McpScopes.HasScope(context.User, McpScopes.Tools)))
            .AddPolicy(McpScopes.Write, policy => policy.RequireAssertion(
                context => !enforced || McpScopes.HasScope(context.User, McpScopes.Write)));
    }

    private static IEnumerable<string> BoundAddresses(WebApplication app)
    {
        return app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            ?? [];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = ListeningMessagePrefix + "{Address}")]
    private static partial void LogListening(ILogger logger, string address);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Refused a request from disallowed origin {Origin}")]
    private static partial void LogOriginRejected(ILogger logger, string origin);
}
