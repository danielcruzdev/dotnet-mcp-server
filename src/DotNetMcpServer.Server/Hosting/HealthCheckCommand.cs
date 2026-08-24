namespace DotNetMcpServer.Server.Hosting;

/// <summary>
/// Asks a server already running in this container whether it is serving, and reports the
/// answer as an exit code.
/// </summary>
/// <remarks>
/// This exists so the image needs no <c>curl</c>. The ASP.NET runtime image ships without one,
/// and adding a shell utility to a production image purely to answer a health probe trades
/// attack surface for convenience. The binary is already there and already knows how to make
/// an HTTP request.
/// </remarks>
internal static class HealthCheckCommand
{
    public const string Argument = "--health-check";

    /// <summary>The unauthenticated endpoint the probe asks for.</summary>
    internal const string Path = "/health";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static bool IsRequested(string[] args)
    {
        return Array.Exists(args, argument => argument.Equals(Argument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>0 when the server answered, 1 when it did not.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        using var client = new HttpClient { Timeout = Timeout };

        try
        {
            using var response = await client.GetAsync(new Uri(ProbeAddress(args)));

            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            // The timeout. A server that cannot answer in three seconds is not healthy.
            return 1;
        }
    }

    /// <summary>
    /// Turns the address the server was told to bind into one that can be dialled from inside
    /// the same container.
    /// </summary>
    /// <remarks>
    /// A container binds every interface, and the wildcard forms are not connectable
    /// addresses — <c>http://+:3001</c> is not a URL at all. Each maps to loopback, which is
    /// where a probe running beside the server should be knocking anyway.
    /// </remarks>
    private static string ProbeAddress(string[] args)
    {
        var configured = ReadUrls(args) ?? HttpServerHost.DefaultUrls;
        var first = configured.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('/');

        foreach (var wildcard in (string[])["0.0.0.0", "[::]", "+", "*"])
        {
            first = first.Replace("://" + wildcard, "://127.0.0.1", StringComparison.Ordinal);
        }

        return first + Path;
    }

    private static string? ReadUrls(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--urls", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    }
}
