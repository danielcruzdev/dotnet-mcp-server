# CLAUDE.md

## The project

A production-shaped MCP server and AI agent in .NET 10, built on the **official
`ModelContextProtocol` SDK** (Tier 1, maintained with Microsoft) — with a hand-written
protocol implementation kept alongside it as a study artifact.

The pitch is not "I implemented a protocol." It is: *implemented the stdio transport by hand
to understand it, proved it interoperates, then shipped on the official SDK — because the spec
ships a new revision every few months.* Depth **and** judgement. Read `.specs/PRD.md` §4 for
the full reasoning; it also records that an earlier revision argued the opposite and why that
was reversed.

## Hard constraints

These break silently. Every one has already cost real time.

**stdout belongs to the MCP protocol.** No `Console.WriteLine` anywhere in
`DotNetMcpServer.Server`. Logging goes through `ILogger`, pinned to stderr in `Program.cs` via
`LogToStandardErrorThreshold`. A single stray write corrupts the session for every client.

**Never launch the server with `dotnet run`.** MSBuild writes to stdout — the protocol
channel. `AgentSettingsLoader.ResolveServerCommand` resolves the compiled binary; keep it that
way, including in docs and client config.

**`src/Mcp.Protocol.Handwritten/` is frozen.** A study artifact, referenced by nothing
shipped. Correcting it is in scope; extending it to new spec revisions is an explicit non-goal.
Its types — `IMcpTool`, `McpToolCallResult`, `ToolRegistry` — must never appear in shipped code.

**`InvariantGlobalization` stays `false`.** Turning it on disables ICU, which breaks IANA
timezone resolution in `get_current_datetime`. It is pinned with a comment in
`Directory.Build.props`. This has already happened once.

**English only** in `src/` — comments, log messages, exception text, identifiers. The
migration away from Portuguese is deliberate (finding D3).

**Zero warnings.** `TreatWarningsAsErrors` is on with analyzers. A warning is a broken build,
not a suggestion.

## Commands

```bash
dotnet build DotNetMcpServer.slnx --nologo        # must be 0 warnings, 0 errors
dotnet test  DotNetMcpServer.slnx --nologo -v q
```

`tests/DotNetMcpServer.Tests/Integration/` launches the built server as a real subprocess and
drives it with the official SDK client. **That is the only check that proves the server works** —
unit tests say nothing about protocol reachability, and piping JSON at the binary produces a
false negative. See the `verify-mcp-server` skill before diagnosing any protocol problem.

## Layout

```
src/DotNetMcpServer.Server/       SDK-based MCP server; tools are [McpServerTool] methods
src/DotNetMcpServer.Agent/        console agent; SDK client + OpenAI tool-calling
src/Mcp.Protocol.Handwritten/     frozen study artifact
tests/DotNetMcpServer.Tests/      Tools (unit) · Protocol (artifact) · Integration (interop)
.specs/PRD.md                     audit of 34 findings + 8-phase roadmap
.specs/PROGRESSO.md               live task tracker + decision log
```

## Working the roadmap

`.specs/PRD.md` holds the reasoning, `.specs/PROGRESSO.md` holds the state. Task ids (`F1-10`,
`F5-07`) are named in commit bodies, not subjects — see the `writing-commits` skill. Mark a
task ✅ only after verification, never at "it compiles", and record regressions honestly — a
tracker that only improves is one nobody trusts.

Deviations from the PRD go in the decision log with their reason, including dropped tasks. That
file is the project's main portfolio artifact after the code. Use the `roadmap-task` skill.

---

# Behavioral guidelines

Reduce common LLM coding mistakes. **Tradeoff:** these bias toward caution over speed. For
trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require
constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to
overcomplication, and clarifying questions come before implementation rather than after
mistakes.
