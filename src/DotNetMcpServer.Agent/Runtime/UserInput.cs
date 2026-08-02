namespace DotNetMcpServer.Agent.Runtime;

/// <summary>
/// Reads one line of user input, abandoning the read when the token is cancelled.
/// </summary>
public interface IUserInput
{
    /// <returns>The line typed by the user, or <see langword="null"/> at end of input.</returns>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
}

public sealed class ConsoleUserInput : IUserInput
{
    private readonly TextReader _reader;

    public ConsoleUserInput()
        : this(Console.In)
    {
    }

    public ConsoleUserInput(TextReader reader)
    {
        _reader = reader;
    }

    /// <remarks>
    /// A console read cannot be cancelled. <see cref="Console.In"/> is a synchronous reader, so
    /// even <c>ReadLineAsync(token)</c> blocks the calling thread until a line arrives — which
    /// is why Ctrl+C used to set the token and change nothing.
    /// <para>
    /// The read runs on a thread-pool thread and the <em>wait</em> is cancelled instead. The
    /// read itself stays parked on stdin until the process exits; one leaked thread during
    /// shutdown is the price of a console that responds to Ctrl+C.
    /// </para>
    /// </remarks>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var read = Task.Run(_reader.ReadLine, CancellationToken.None);

        return await read.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
