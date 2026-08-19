using System.Diagnostics;
using DotNetMcpServer.Probe;
using DotNetMcpServer.Probe.Phases;

// A development tool for closing a phase: it drives the compiled server with the official SDK
// client, prints what came back, and ends with a verdict. `EXAMPLES.md` rotted because Markdown
// does not compile; this is in the solution, so it breaks the build when the server's result
// types move, and it is run, so it fails loudly when anything else does.
//
// Running it with `dotnet run` is fine. The prohibition in CLAUDE.md is about launching the MCP
// *server*, whose stdout carries the protocol — the server this spawns is always the compiled
// binary resolved by ServerBinary.

if (args.Length == 0)
{
    Usage();

    return 0;
}

var phase = args[0].ToLowerInvariant();
var runTests = args.Contains("--tests", StringComparer.Ordinal);
var capability = args.Skip(1).FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));

using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    lifetime.Cancel();
};

try
{
    return phase switch
    {
        "phase1" when runTests => await RunTestsAsync("1", lifetime.Token),
        "phase1" => await ProbeAsync(
            "Phase 1", "phase1", Phase1Probe.Capabilities, capability, Phase1Probe.RunAsync, lifetime.Token),

        "phase2" => Phase2(),

        "phase3" when runTests => await RunTestsAsync("3", lifetime.Token),
        "phase3" => await ProbeAsync(
            "Phase 3", "phase3", Phase3Probe.Capabilities, capability, Phase3Probe.RunAsync, lifetime.Token),

        _ => Unknown(args[0])
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");

    return 130;
}
catch (Exception exception)
{
    // A development tool: a one-line cause is more useful at the prompt than a stack trace,
    // and the exit code still says it failed.
    Console.Error.WriteLine($"The probe failed: {exception.Message}");

    return 1;
}

static async Task<int> ProbeAsync(
    string title,
    string command,
    IReadOnlyList<string> capabilities,
    string? capability,
    Func<Report, string?, CancellationToken, Task> run,
    CancellationToken cancellationToken)
{
    if (capability is not null && !capabilities.Contains(capability, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"Unknown capability '{capability}'. Try: {string.Join(", ", capabilities)}");

        return 2;
    }

    var report = new Report();
    await run(report, capability, cancellationToken);

    return report.Verdict(title, command);
}

static async Task<int> RunTestsAsync(string phase, CancellationToken cancellationToken)
{
    // Phases map to tests through [Trait("Phase", "N")], not through a filter string naming
    // classes: the trait lives next to the test, so renaming or moving one carries the mapping
    // with it. A filter string would rot exactly the way EXAMPLES.md did.
    var info = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = ServerBinary.RepositoryRoot,
        UseShellExecute = false
    };

    info.ArgumentList.Add("test");
    info.ArgumentList.Add(ServerBinary.SolutionPath);
    info.ArgumentList.Add("--nologo");
    info.ArgumentList.Add("--filter");
    info.ArgumentList.Add($"Phase={phase}");

    Console.WriteLine($"dotnet test DotNetMcpServer.slnx --filter \"Phase={phase}\"");
    Console.WriteLine();

    using var process = Process.Start(info)
        ?? throw new InvalidOperationException("Could not start 'dotnet test'.");

    await process.WaitForExitAsync(cancellationToken);

    return process.ExitCode;
}

static int Phase2()
{
    // Phase 2 is agent architecture: Generic Host, DI, validated IOptions, a resilience
    // pipeline. None of it produces observable protocol behaviour, so a `probe phase2` would be
    // a demonstration invented to fill a row in a table. What Phase 2 does produce that anyone
    // can watch is below. Uniform coverage across phases is not a goal.
    Console.WriteLine("Phase 2 has no probe, on purpose — it changed the agent's architecture,");
    Console.WriteLine("not the server's protocol surface, so there is nothing on the wire to show.");
    Console.WriteLine();
    Console.WriteLine("What is observable is startup validation. With OPENAI_API_KEY unset:");
    Console.WriteLine();
    Console.WriteLine("  src/DotNetMcpServer.Agent/bin/Debug/net10.0/DotNetMcpServer.Agent");
    Console.WriteLine();
    Console.WriteLine("fails before the first request, with the member that is missing named,");
    Console.WriteLine("and exits 82. See examples/EXAMPLES.md.");
    Console.WriteLine();
    Console.WriteLine("Its tests: probe phase2 --tests is not offered; run");
    Console.WriteLine("  dotnet test DotNetMcpServer.slnx --filter \"Phase=2\"");

    return 0;
}

static int Unknown(string phase)
{
    Console.Error.WriteLine($"Unknown phase '{phase}'.");
    Console.Error.WriteLine();
    Usage();

    return 2;
}

static void Usage()
{
    Console.WriteLine("Exercises what each phase delivered, against the compiled server.");
    Console.WriteLine();
    Console.WriteLine("  probe phase1 [capability] [--tests]   SDK migration: it talks to a real client");
    Console.WriteLine("  probe phase2                          why this phase has no probe");
    Console.WriteLine("  probe phase3 [capability] [--tests]   every MCP capability, not just tools");
    Console.WriteLine();
    Console.WriteLine($"  phase1 capabilities: {string.Join(", ", Phase1Probe.Capabilities)}");
    Console.WriteLine($"  phase3 capabilities: {string.Join(", ", Phase3Probe.Capabilities)}");
    Console.WriteLine();
    Console.WriteLine("--tests runs that phase's interop suite, which is what proves the server");
    Console.WriteLine("works. The probe demonstrates; the suite proves.");
}
