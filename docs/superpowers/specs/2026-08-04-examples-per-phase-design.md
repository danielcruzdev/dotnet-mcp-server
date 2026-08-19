# Per-phase examples — design

**Date:** 2026-08-04
**Status:** implemented 2026-08-18. One deviation: checks that write a note or edit a document
build their own temp workspace rather than pointing at `examples/workspace/`, which would
otherwise leave untracked files in the repository on every run. Read-only checks still use it,
which is what the "real documents" reasoning below was about. See the decision log in
`.specs/PROGRESSO.md`.

## The problem

`examples/` has rotted. `EXAMPLES.md` documents four tools and a protocol surface that stopped
being accurate when Phase 3 landed: there is no `scan_workspace`, no resources, prompts,
completion, logging, progress, structured output, elicitation or sampling in it. It closes by
pointing at `tests/DotNetMcpServer.Tests/Examples/ScenarioTests.cs`, which does not exist.

It rotted for a structural reason, not a careless one: **Markdown does not compile.** Nothing in
the build fails when a tool is renamed out from under a document, so the document quietly
becomes wrong and stays wrong.

The goal is a way to exercise each phase by hand — see what the server actually answered, not
just a green tick — that resists the same decay.

## Audience and purpose

A development tool for the repository owner, used when closing a phase. It optimises for being
fast to run and cheap to extend, one file per phase. A reader browsing the repository benefits
from it, but that is not what it is shaped for.

Everything new is written in **English**, code and prose. The repository is mid-migration away
from Portuguese (finding D3, task `F2-09`), and `F8-06` will rewrite the README in English.
Writing this in Portuguese would create the problem that phase exists to solve.

## Approach

A console project, `examples/probe/` (`DotNetMcpServer.Probe.csproj`), **inside
`DotNetMcpServer.slnx`**. It drives the compiled server with the official SDK client, prints
what came back, and ends with a verdict.

Being in the solution is the point, and it is worth being precise about how much that buys.

The probe calls tools by name over the protocol, so **renaming a tool is not a compile error** —
it is a runtime failure. What the project reference to `DotNetMcpServer.Server` does buy is
compile-time coverage of the server's public result types: if `WorkspaceScan` gains, loses or
renames a property, the probe stops building. The rest is caught by the probe being *executed*,
which is the real difference from Markdown. A document is never run; this is run once per phase
and fails loudly.

Two alternatives were rejected for failing that test:

- **Per-phase shell scripts** — no compilation, so they rot the same way `EXAMPLES.md` did.
- **Example tests inside the existing test project** — cheap, but a test runner swallows
  console output. Reading the values back would mean `-v normal` and hunting through the log,
  which defeats the purpose.

## Layout

```
examples/
├── EXAMPLES.md                  index: what each phase delivered + how to exercise it
├── probe/
│   ├── DotNetMcpServer.Probe.csproj
│   ├── Program.cs               <phase> [capability] [--tests]
│   ├── ServerBinary.cs          resolves the compiled binary; never `dotnet run`
│   ├── Report.cs                printing and the PASS/FAIL tally
│   └── Phases/
│       ├── Phase1Probe.cs       handshake, tools/list, tools/call, containment refusal
│       └── Phase3Probe.cs       resources, templates, subscribe, prompts, completion,
│                                logging, progress, structured output, elicitation, sampling
├── workspace/                   real documents the server reads (already present)
└── jsonrpc/                     raw frames; kept, relabelled as illustrating the artifact
```

Each future phase adds one file under `Phases/` and one row in the index.

## Command surface

Written below as `probe`, which in practice is either of:

```
dotnet run --project examples/probe -- phase3
examples/probe/bin/Debug/net10.0/DotNetMcpServer.Probe[.exe] phase3
```

`dotnet run` is safe *here* — the prohibition in `CLAUDE.md` is about launching the MCP server,
whose stdout carries the protocol. The probe's stdout is a console, and the server it spawns is
always the compiled binary resolved by `ServerBinary`.

```
probe phase3                     every Phase 3 check
probe phase3 resources           one capability
probe phase3 --tests             the interop tests carrying [Trait("Phase", "3")]
probe                            lists what can be probed
```

## Four decisions worth their own paragraph

### Phase 2 gets no probe

Phase 2 is agent architecture: Generic Host, DI, validated `IOptions`, a resilience pipeline.
None of it produces observable protocol behaviour, so a `probe phase2` would be a demonstration
invented to fill a row in a table.

What Phase 2 gets in `EXAMPLES.md` is the thing that *is* observable: start the agent with no
`OPENAI_API_KEY` and watch it fail **at startup** with a validation message rather than at the
first call. That is Phase 2 working, and it is honest. Uniform coverage across phases is not a
goal; a hollow example is worse than an absent one.

### The probe's verdict is not the proof

`CLAUDE.md` and the `verify-mcp-server` skill are explicit that the interop suite is the only
thing that proves the server works. A tool that prints `PASS` can, over time, become the thing
people run *instead of* the tests. Two defences:

- The probe uses the **official SDK client against the compiled binary** — the same mechanics
  as the interop suite. It does not fall into the shell-pipe trap that skill warns about, so
  its `PASS` means something.
- `--tests` runs that phase's suite, and `EXAMPLES.md` states in one line which of the two is
  the proof. The probe demonstrates; the suite proves.

### `--tests` maps phases through traits, not a filter string

Something has to map phase → tests. A filter string like `"FullyQualifiedName~Resource|..."`
rots the way `EXAMPLES.md` rotted. Instead the interop classes carry `[Trait("Phase", "3")]`
and the filter is `--filter "Phase=3"`. The trait lives next to the test, so renaming or moving
a class carries it along.

This costs an edit to roughly twelve existing test files. It is the only change in this design
that touches code that already works, and it is accepted because the alternative is a mapping
that silently drifts.

### The probe has no tests of its own

It is a development tool. Testing the tester is the speculative work `CLAUDE.md` §2 exists to
cut. It is exercised by being run, and it compiles in CI, which is what stops it from rotting.

Likewise there is **no transport abstraction**. Phase 4 brings Streamable HTTP; each probe opens
its own connection and the shared harness only resolves the binary and prints. `Phase4Probe.cs`
will connect over HTTP without anything needing to be generalised ahead of time.

## `EXAMPLES.md`

Rewritten now to cover Phases 1–3 and to act as the index. This absorbs part of `F8-06`, which
the decision log currently assigns the rewrite to; the deviation gets its own decision-log entry
naming `F8-06` so the roadmap does not silently lose the task.

`examples/jsonrpc/` stays as it is. The frames are incomplete rather than wrong, and they
illustrate the hand-written artifact's newline framing, which is still accurate. Regenerating
them is not in scope.

## Workspace and determinism

The probe points the server at `examples/workspace/`, which holds real, readable documents —
better for a demonstration than a temp directory of generated files. Because that directory can
gain files, the probe **reports counts rather than asserting exact numbers**. Checks that need
an exact count create their own temp workspace.

## Success criteria

1. `dotnet build DotNetMcpServer.slnx` stays at 0 warnings with the probe in the solution.
2. `probe phase1` and `probe phase3` connect to the compiled binary and print real responses.
3. `probe phase3 --tests` runs exactly the Phase 3 interop tests, and no others.
4. Changing a property on a server result type breaks the probe's build; renaming a tool fails
   the probe at runtime with a message naming the tool. Both are verified deliberately, because
   the first was overstated in an earlier draft of this document.
5. `EXAMPLES.md` describes Phases 1–3 accurately and names the interop suite as the proof.
6. The decision log records the `F8-06` overlap.
