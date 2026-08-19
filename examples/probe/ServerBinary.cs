namespace DotNetMcpServer.Probe;

/// <summary>
/// Resolves the compiled server executable and the repository paths the probe reads from.
/// </summary>
/// <remarks>
/// The server is always launched as a compiled binary, never through <c>dotnet run</c>: MSBuild
/// writes to stdout, which is the channel the MCP protocol owns. This mirrors what
/// <c>ServerLocator</c> does for the interop suite on purpose — a probe is only worth trusting
/// if it starts the server the same way a real client does.
/// </remarks>
internal static class ServerBinary
{
    private static readonly string Root = FindRepositoryRoot();

    /// <summary>The repository root, found by walking up from the probe's own output directory.</summary>
    public static string RepositoryRoot => Root;

    public static string SolutionPath => Path.Combine(Root, "DotNetMcpServer.slnx");

    /// <summary>
    /// Real documents rather than a temp directory of generated files, which is what makes the
    /// output worth reading. Because the directory can gain files, checks against it report
    /// counts instead of asserting numbers.
    /// </summary>
    public static string ExampleWorkspace => Path.Combine(Root, "examples", "workspace");

    /// <summary>
    /// The server executable built into the same configuration and target framework as the probe.
    /// </summary>
    public static string ServerPath()
    {
        // .../bin/<configuration>/<tfm> — take the two segments back from the probe's output.
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = output.Name;
        var configuration = output.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the build configuration.");

        var fileName = OperatingSystem.IsWindows()
            ? "DotNetMcpServer.Server.exe"
            : "DotNetMcpServer.Server";

        var path = Path.Combine(
            Root, "src", "DotNetMcpServer.Server", "bin", configuration, targetFramework, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Expected the server at '{path}'. Run: dotnet build DotNetMcpServer.slnx", path);
        }

        return path;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (current.GetFiles("*.slnx").Length > 0 || Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the probe's output directory.");
    }
}
