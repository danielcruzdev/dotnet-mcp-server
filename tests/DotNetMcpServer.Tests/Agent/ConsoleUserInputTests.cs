using DotNetMcpServer.Agent.Runtime;

namespace DotNetMcpServer.Tests.Agent;

/// <summary>
/// Ctrl+C sets the cancellation token, and before this the agent sat on a blocking console
/// read and ignored it. These tests pin the read's contract: it yields on cancellation, and it
/// reports end of input as <c>null</c> rather than as an empty line.
/// </summary>
public sealed class ConsoleUserInputTests
{
    /// <summary>
    /// Stands in for a console with a user who has not typed anything yet.
    /// </summary>
    private sealed class BlockingReader : TextReader
    {
        private readonly ManualResetEventSlim _release;

        public BlockingReader(ManualResetEventSlim release)
        {
            _release = release;
        }

        public override string? ReadLine()
        {
            _release.Wait();
            return null;
        }
    }

    [Fact]
    public async Task ReadLineAsync_returns_the_line_that_was_typed()
    {
        using var reader = new StringReader("what does tools/list return?");
        var input = new ConsoleUserInput(reader);

        Assert.Equal("what does tools/list return?", await input.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_reports_end_of_input_as_null()
    {
        using var reader = new StringReader(string.Empty);
        var input = new ConsoleUserInput(reader);

        Assert.Null(await input.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_gives_up_when_the_token_is_cancelled()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        using var reader = new BlockingReader(release);
        using var cancellation = new CancellationTokenSource();

        var input = new ConsoleUserInput(reader);
        var pending = input.ReadLineAsync(cancellation.Token);

        Assert.False(pending.IsCompleted);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // The read itself is still parked, exactly as it would be on a real console. Releasing
        // it here keeps the thread-pool thread from outliving the test.
        release.Set();
    }
}
