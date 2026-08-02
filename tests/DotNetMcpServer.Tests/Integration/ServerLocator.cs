using System.Runtime.InteropServices;

namespace DotNetMcpServer.Tests.Integration;

/// <summary>
/// Locates built server executables so integration tests can launch them as real
/// subprocesses, the same way an MCP client would.
/// </summary>
internal static class ServerLocator
{
    /// <summary>
    /// Resolves the executable for a project built into the same configuration and target
    /// framework as the test assembly.
    /// </summary>
    public static string ExecutablePath(string projectName)
    {
        var repositoryRoot = FindRepositoryRoot();
        var testDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        // .../bin/<configuration>/<tfm>  -> take the two segments back from the test output.
        var targetFramework = testDirectory.Name;
        var configuration = testDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the build configuration.");

        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? projectName + ".exe"
            : projectName;

        var path = Path.Combine(repositoryRoot, "src", projectName, "bin", configuration, targetFramework, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Expected '{projectName}' to be built at '{path}'. Build the solution before running integration tests.",
                path);
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

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
