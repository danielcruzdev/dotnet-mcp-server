using System.Globalization;
using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Server.Workspace;
using ModelContextProtocol;

namespace DotNetMcpServer.Tests.Tools;

/// <summary>
/// Unit coverage for the logic behind each tool. The MCP surface itself is covered by
/// <see cref="Integration.SdkServerInteropTests"/>, which drives the real server with the
/// official client — so these tests stay focused on behaviour, not on protocol plumbing.
/// </summary>
public sealed class ToolLogicTests
{
    [Fact]
    public void GetCurrentDateTime_without_timezone_reports_utc()
    {
        var moment = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        var result = DateTimeTools.Describe(moment, timezone: null);

        Assert.Equal("UTC", result.TimeZone);
        Assert.StartsWith("2026-07-28T12:00:00", result.Iso8601, StringComparison.Ordinal);
        Assert.Contains("28 Jul 2026", result.Formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void GetCurrentDateTime_converts_to_the_requested_timezone()
    {
        var moment = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        var result = DateTimeTools.Describe(moment, "America/Sao_Paulo");

        Assert.Equal("America/Sao_Paulo", result.TimeZone);

        // Same instant, three hours behind UTC.
        Assert.StartsWith("2026-07-28T09:00:00", result.Iso8601, StringComparison.Ordinal);
        Assert.Contains("-03:00", result.Iso8601, StringComparison.Ordinal);
    }

    [Fact]
    public void GetCurrentDateTime_rejects_an_unknown_timezone()
    {
        var exception = Assert.Throws<McpException>(
            () => DateTimeTools.Describe(DateTimeOffset.UtcNow, "Mars/Olympus_Mons"));

        Assert.Contains("Mars/Olympus_Mons", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 + 1", 2)]
    [InlineData("(1200 + 350) / 5", 310)]
    [InlineData("2 * (3 + 4)", 14)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("1200 + 85", 1285)]
    public void CalculateExpression_evaluates_arithmetic(string expression, double expected)
    {
        var result = CalculatorTools.Evaluate(expression);

        Assert.Equal(expression, result.Expression);
        Assert.Equal((decimal)expected, result.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CalculateExpression_requires_an_expression(string expression)
    {
        Assert.Throws<McpException>(() => CalculatorTools.Evaluate(expression));
    }

    [Theory]
    [InlineData("System.IO.File.Delete('x')")]
    [InlineData("1 + DROP TABLE users")]
    [InlineData("SUM(price)")]
    public void CalculateExpression_rejects_anything_that_is_not_arithmetic(string expression)
    {
        var exception = Assert.Throws<McpException>(() => CalculatorTools.Evaluate(expression));

        Assert.Contains("Invalid expression", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEntry_renders_the_title_and_body()
    {
        var createdAt = new DateTimeOffset(2026, 7, 28, 9, 30, 0, TimeSpan.Zero);

        var entry = WorkspaceTools.FormatEntry("MCP study", "  Review tools/list.  ", createdAt);

        Assert.Contains("## MCP study", entry, StringComparison.Ordinal);
        Assert.Contains("2026-07-28 09:30:00", entry, StringComparison.Ordinal);
        Assert.Contains("Review tools/list.", entry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatEntry_falls_back_to_a_default_title(string? title)
    {
        var entry = WorkspaceTools.FormatEntry(title, "body", DateTimeOffset.UtcNow);

        Assert.Contains("## Note", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePath_maps_a_relative_path_inside_the_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N"));
        var workspace = new WorkspaceContext(root);

        var resolved = workspace.ResolvePath(Path.Combine("docs", "guide.md"));

        Assert.StartsWith(workspace.Root, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("guide.md", resolved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("docs/../../../secret.txt")]
    public void ResolvePath_refuses_to_escape_the_workspace(string relativePath)
    {
        var root = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N"), "nested");
        var workspace = new WorkspaceContext(root);

        Assert.Throws<UnauthorizedAccessException>(() => workspace.ResolvePath(relativePath));
    }

    /// <summary>
    /// The directory is created on the call that needs it, not when the workspace is wired up.
    /// Constructing the context must stay free of side effects.
    /// </summary>
    [Fact]
    public void EnsureDirectory_creates_the_directory_only_when_it_is_asked_for()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N"));

        var workspace = new WorkspaceContext(root);
        Assert.False(Directory.Exists(root));

        try
        {
            var created = workspace.EnsureDirectory("notes");

            Assert.True(Directory.Exists(created));
            Assert.Equal(Path.Combine(workspace.Root, "notes"), created);

            // Creating it a second time is not an error — the tool calls this on every note.
            Assert.Equal(created, workspace.EnsureDirectory("notes"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureDirectory_refuses_to_create_anything_outside_the_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N"), "nested");
        var workspace = new WorkspaceContext(root);

        Assert.Throws<UnauthorizedAccessException>(() => workspace.EnsureDirectory("../escaped"));
        Assert.False(Directory.Exists(Path.Combine(root, "..", "escaped")));
    }

    [Fact]
    public void Resolve_prefers_the_workspace_root_argument()
    {
        var expected = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        var workspace = WorkspaceContext.Resolve(["--workspace-root", expected]);

        Assert.Equal(Path.GetFullPath(expected), workspace.Root);
    }
}
