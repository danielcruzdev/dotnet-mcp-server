# Examples

What each phase delivered, and how to see it for yourself.

This file used to document four tools and a protocol surface that stopped being accurate when
Phase 3 landed, and it closed by pointing at a test file that did not exist. It rotted for a
structural reason rather than a careless one: **Markdown does not compile**, so nothing failed
when the code moved past it.

So the demonstrations are not in this document. They are in `examples/probe/`, a console project
inside `DotNetMcpServer.slnx` that drives the compiled server with the official SDK client and
prints what came back. It is in the solution, so it breaks the build when the server's result
types move; it is run, so it fails loudly when anything else does. This file is the index.

---

## Layout

```
examples/
├── EXAMPLES.md      this file — what each phase delivered, and how to exercise it
├── probe/           the per-phase probe (a console project in the solution)
│   ├── Program.cs       <phase> [capability] [--tests]
│   ├── ServerBinary.cs  resolves the compiled binary; never `dotnet run`
│   ├── Report.cs        printing and the PASS/FAIL tally
│   └── Phases/          one file per phase
├── workspace/       real documents the server reads — the probe points at these
└── jsonrpc/         raw request/response frames, kept as an illustration of the
                     hand-written artifact's newline framing (see the note at the end)
```

---

## Running it

Build first — the probe launches the **compiled** server binary, never `dotnet run`, because
MSBuild writes to stdout and stdout is the channel the protocol owns.

```bash
dotnet build DotNetMcpServer.slnx
dotnet run --project examples/probe -- phase3
```

`dotnet run` is safe *here*: the probe's stdout is a console, and the server it spawns is
always the compiled binary. Or call the binary directly:

```
examples/probe/bin/Debug/net10.0/DotNetMcpServer.Probe[.exe] phase3
```

| Command | What it does |
|---|---|
| `probe` | lists what can be probed |
| `probe phase3` | every Phase 3 check |
| `probe phase3 resources` | one capability |
| `probe phase3 --tests` | that phase's interop suite, via `[Trait("Phase", "3")]` |

**The probe demonstrates. The interop suite proves.** `CLAUDE.md` and the `verify-mcp-server`
skill are explicit that the suite driving the real binary is the only thing that establishes the
server works, and a tool that prints `PASS` can quietly become the thing people run instead. The
probe's verdict means something — it uses the same client against the same binary — but `--tests`
is there so the proof is one flag away, and the verdict says so every run.

---

## Phase 1 — SDK migration

**What it delivered:** a server a client that is not this repository can talk to. Before it, the
transport framed messages with an LSP-style `Content-Length` header instead of the newline
delimiting MCP requires, so nothing but the agent in this repo could connect.

```
probe phase1                 handshake · tools · call · containment
```

```
--- Phase 1 - call -----------------------------------------------------------
  calculate_expression          (1200 + 350) / 5 = 310
  PASS  an expression is evaluated and returned as data
  get_current_datetime          2026-08-18T21:47:21-03:00  (Tuesday, 18 Aug 2026 21:47:21 -03:00)
  PASS  an IANA timezone resolves — which is why InvariantGlobalization stays false
```

Five tools are advertised: `append_study_note`, `calculate_expression`, `get_current_datetime`,
`read_text_file`, `scan_workspace`.

**Not probed, deliberately:** the hand-written artifact in `src/Mcp.Protocol.Handwritten/`, which
is Phase 1's other half. It is driven by `HandwrittenServerInteropTests` — the official SDK client
against the hand-written server, which is what turns *"I implemented the protocol"* from an
assertion into evidence. `probe phase1 --tests` runs it, along with the SDK server's own interop
cases: **13 tests**.

---

## Phase 2 — Modern .NET architecture

**Phase 2 has no probe, on purpose.** It adopted the Generic Host, DI, validated `IOptions`, and
a resilience pipeline — in the *agent*. None of that produces observable protocol behaviour, so a
`probe phase2` would be a demonstration invented to fill a row in this table. Uniform coverage
across phases is not a goal; a hollow example is worse than an absent one.

What *is* observable is startup validation. Run the agent with `OPENAI_API_KEY` unset:

```
src/DotNetMcpServer.Agent/bin/Debug/net10.0/DotNetMcpServer.Agent
```

```
Hosting failed to start
Microsoft.Extensions.Options.OptionsValidationException: DataAnnotation validation failed for
'OpenAiSettings' members: 'ApiKey' with the error: 'The ApiKey field is required.'.
```

Exit code 82, before the first request rather than at it, with the missing member named.

```bash
dotnet test DotNetMcpServer.slnx --filter "Phase=2"     # 5 tests
```

---

## Phase 3 — The full MCP surface

**What it delivered:** every MCP capability, not just tools.

```
probe phase3                 all ten capabilities
probe phase3 <capability>    one of them
```

| Capability | What the probe shows |
|---|---|
| `resources` | workspace documents listed with uri, mime type and size, then read back |
| `templates` | `workspace://excerpt/{start}-{end}/{+path}` expanding into a line range |
| `subscriptions` | an edit to a subscribed document arriving as a notification |
| `prompts` | `study_plan(topic, [hoursPerWeek])`, `summarize_document(path, [audience])` |
| `completion` | a `path` argument completing from documents the server can actually read |
| `logging` | `logging/setLevel` opening the `notifications/message` stream |
| `progress` | one report per document walked by `scan_workspace`, the last one included |
| `structured` | `outputSchema` + `structuredContent`, and every tool's annotations |
| `elicitation` | a tool asking the user for the argument it was not given |
| `sampling` | the server borrowing the client's model to name a note |

### The finding that outlived the phase

Two of those capabilities are **already gone from the revision the SDK negotiates by default**.
The probe shows each one twice — working on `2025-11-25`, the revision every shipping client
still negotiates, and refused on `2026-07-28`:

```
--- Phase 3 - logging --------------------------------------------------------
  on 2025-11-25                 logging/setLevel accepted
  notifications/message         [Info] DotNetMcpServer.Server.Tools.WorkspaceTools
      Read dotnet-concepts.md: 1621 characters returned, truncated=True
  PASS  only this server's own log categories are forwarded, so the bridge terminates
  SEP-2577 moved the level onto per-request _meta on 2026-07-28.
  refusal                       Request failed (remote): The method 'logging/setLevel' is not
                                available on protocol version '2026-07-28'. Use the per-request
                                '_meta/io.modelcontextprotocol/logLevel' field instead.
  PASS  the current revision refuses it and names its replacement
```

`resources/subscribe` (SEP-2575) behaves the same way and names `subscriptions/listen`. Neither
refusal is a defect in this server — it is the specification moving, found by a test failing six
days after the audit that argued spec velocity was the reason to build on the official SDK. See
the decision log in [`.specs/PROGRESSO.md`](../.specs/PROGRESSO.md).

```bash
probe phase3 --tests     # 66 tests
```

---

## What the probe does not do

- **It is not the proof.** `probe phase3 --tests` is. Of the **162** tests in the suite, 84 drive
  a real client against a real server subprocess.
- **It has no tests of its own.** It is a development tool; testing the tester is speculative
  work. It is exercised by being run and it compiles in CI, which is what stops it from rotting.
- **It does not assert exact counts** against `examples/workspace/`, which can gain files. It
  reports what came back and derives its expectations from the answer — the progress check waits
  for as many reports as `scan_workspace` said it walked. Checks that write a note or edit a
  document build their own temp workspace, so running the probe leaves the repository clean.
- **Renaming a tool does not break its build.** Tools are called by name over the wire, so that
  is a runtime failure — `The probe failed: Request failed (remote): Unknown tool: 'scan_workspace'`,
  exit code 1. What the project reference *does* buy is the compile-time half: rename a property
  on `WorkspaceScan`, `CalculationResult` or `CurrentDateTime` and the probe stops building.

---

## `examples/jsonrpc/`

Fifteen raw request/response frames, kept as they are. They predate Phase 3 and cover only the
original four tools, so they are **incomplete rather than wrong** — and what they illustrate,
newline-delimited framing with one JSON object per line, is still exactly how the transport
works. They are best read as an illustration of the hand-written artifact rather than as a
current capability list; the probe is the current capability list. Regenerating them is not
planned.
