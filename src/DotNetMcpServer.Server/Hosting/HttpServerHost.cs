using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Loopback by default. A remote MCP server that binds every interface the moment it
    /// starts is how a local tool server becomes a public one by accident — the spec calls out
    /// localhost binding for exactly this reason.
    /// </summary>
    private const string DefaultUrls = "http://127.0.0.1:3001";

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

        builder.Services
            .AddWorkspaceMcpServer(args)
            .WithHttpTransport(options =>
            {
                // SEP-2567 made stateless the SDK default. This server tracks sessions on
                // purpose: F4-03's Last-Event-ID replay has nothing to resume without one.
                options.Stateless = false;
            });

        var app = builder.Build();

        app.MapMcp(EndpointPattern);

        await app.StartAsync();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HttpServerHost).FullName!);
        foreach (var address in BoundAddresses(app))
        {
            LogListening(logger, address + EndpointPattern);
        }

        await app.WaitForShutdownAsync();
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
}
