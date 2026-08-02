using DotNetMcpServer.Agent.Config;
using Microsoft.Extensions.Options;

namespace DotNetMcpServer.Tests.Agent;

/// <summary>
/// Path and launch-command derivation is not configuration binding — it runs after the
/// values are bound. These tests pin the behaviour that used to live in the static
/// <c>AgentSettingsLoader</c>, including the one that matters most: a blank command must
/// resolve to the compiled server binary, never to <c>dotnet run</c>, because MSBuild
/// writes to stdout and stdout is the MCP protocol channel.
/// </summary>
public sealed class McpSettingsSetupTests : IDisposable
{
    private readonly string _repositoryRoot;
    private readonly string _applicationBaseDirectory;

    public McpSettingsSetupTests()
    {
        _repositoryRoot = Path.Combine(Path.GetTempPath(), $"mcp-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, ".git"));

        _applicationBaseDirectory = Path.Combine(
            _repositoryRoot, "src", "DotNetMcpServer.Agent", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(_applicationBaseDirectory);
    }

    private McpSettingsSetup CreateSetup() => new(_applicationBaseDirectory, _repositoryRoot);

    [Fact]
    public void Blank_command_resolves_to_the_compiled_server_binary()
    {
        var settings = new McpSettings { Command = string.Empty };

        CreateSetup().PostConfigure(Options.DefaultName, settings);

        var expectedExecutable = OperatingSystem.IsWindows()
            ? "DotNetMcpServer.Server.exe"
            : "DotNetMcpServer.Server";

        Assert.Equal(expectedExecutable, Path.GetFileName(settings.Command));
        Assert.Contains(Path.Combine("bin", "Debug", "net10.0"), settings.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_command_never_resolves_to_dotnet_run()
    {
        var settings = new McpSettings { Command = string.Empty, Arguments = string.Empty };

        CreateSetup().PostConfigure(Options.DefaultName, settings);

        Assert.NotEqual("dotnet", Path.GetFileNameWithoutExtension(settings.Command));
        Assert.DoesNotContain("run", settings.ArgumentList, StringComparer.Ordinal);
    }

    [Fact]
    public void An_explicitly_configured_command_is_left_alone()
    {
        var settings = new McpSettings { Command = "/usr/local/bin/my-server" };

        CreateSetup().PostConfigure(Options.DefaultName, settings);

        Assert.Equal("/usr/local/bin/my-server", settings.Command);
    }

    [Fact]
    public void A_relative_workspace_root_is_anchored_to_the_repository_root()
    {
        var settings = new McpSettings { WorkspaceRoot = "examples/workspace" };

        CreateSetup().PostConfigure(Options.DefaultName, settings);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_repositoryRoot, "examples", "workspace")),
            settings.WorkspaceRoot);
    }

    [Fact]
    public void An_absolute_workspace_root_is_preserved()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "explicit-workspace"));
        var settings = new McpSettings { WorkspaceRoot = absolute };

        CreateSetup().PostConfigure(Options.DefaultName, settings);

        Assert.Equal(absolute, settings.WorkspaceRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
    }
}
