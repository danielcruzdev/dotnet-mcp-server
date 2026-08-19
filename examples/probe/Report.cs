namespace DotNetMcpServer.Probe;

/// <summary>
/// Prints what the server answered and keeps the tally that becomes the exit code.
/// </summary>
/// <remarks>
/// <para>
/// Writing to the console is the whole point. The alternative considered was example tests
/// inside the test project, which is cheaper but hands the output to a test runner that
/// swallows it. The stdout prohibition in <c>CLAUDE.md</c> is about
/// <c>DotNetMcpServer.Server</c>, whose stdout carries the protocol; here stdout is a console.
/// </para>
/// <para>
/// ASCII only: box-drawing characters render as replacement marks on a console that has not
/// been switched to UTF-8, and the probe is meant to be readable wherever it is run.
/// </para>
/// </remarks>
internal sealed class Report
{
    private const int Width = 78;

    private readonly TextWriter _output = Console.Out;

    private int _checks;
    private int _failures;

    public void Section(string title)
    {
        var rule = new string('-', Math.Max(4, Width - title.Length - 5));

        _output.WriteLine();
        _output.WriteLine($"--- {title} {rule}");
    }

    /// <summary>One labelled value the server returned.</summary>
    public void Value(string label, string value)
    {
        _output.WriteLine($"  {label,-30}{value}");
    }

    /// <summary>One entry in a list the server returned.</summary>
    public void Item(string text)
    {
        _output.WriteLine($"      {text}");
    }

    /// <summary>Context that is not itself a value, such as why a check is split in two.</summary>
    public void Note(string text)
    {
        _output.WriteLine($"  {text}");
    }

    public void Check(bool passed, string statement)
    {
        _checks++;

        if (!passed)
        {
            _failures++;
        }

        _output.WriteLine($"  {(passed ? "PASS" : "FAIL")}  {statement}");
    }

    public void Fail(string statement)
    {
        Check(passed: false, statement);
    }

    /// <summary>Prints the tally and returns the process exit code.</summary>
    public int Verdict(string title, string command)
    {
        _output.WriteLine();
        _output.WriteLine(new string('=', Width));
        _output.WriteLine(_failures == 0
            ? $"{title}: {_checks} checks, all passed."
            : $"{title}: {_checks} checks, {_failures} failed.");

        // Said every run, deliberately. A tool that prints PASS becomes the thing people run
        // instead of the tests unless it keeps pointing at them.
        _output.WriteLine("This demonstrates. The interop suite is what proves it:");
        _output.WriteLine($"  probe {command} --tests");

        return _failures == 0 ? 0 : 1;
    }
}
