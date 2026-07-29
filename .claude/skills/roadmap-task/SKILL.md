---
name: roadmap-task
description: Use when working on any task from the project roadmap — when asked to continue the roadmap, start or finish a phase, work on a task id like F1-10 or F5-07, or when a change lands that alters the state recorded in .specs/PROGRESSO.md.
---

# Working a roadmap task

## Core principle

`.specs/PRD.md` holds the reasoning; `.specs/PROGRESSO.md` holds the state. **The tracker is
only worth having if it stays true.** A task marked ✅ that was never verified is worse than
one left ⬜, because it removes the reason to look again.

## The loop

1. **Read the task** in `.specs/PROGRESSO.md`. The **Fixes** column points at the audit finding
   in `.specs/PRD.md` §3 — read that too. The finding explains *why*; the task only says *what*.
2. **Check dependencies.** Each phase header names what it depends on. Some orderings are
   load-bearing and recorded in the decision log (F6-09 before F6-10, for one).
3. **Implement**, honouring the acceptance criteria in the phase's **Done when** block.
4. **Verify.** Build clean, tests green. For anything touching the protocol or a tool,
   **REQUIRED:** use the `verify-mcp-server` skill.
5. **Commit.** Conventional subject, task ids named in the body (`Closes F1-10`) — use the
   `writing-commits` skill.
6. **Update the tracker** — see below.

## Updating the tracker

Mark ✅ only after verification, never at "it compiles". Then, in the same commit:

- Tick the matching **Done when** box if the task completed it
- Update the phase progress bar and the **Overall** count
- Update the **Metrics** table if the change moved any row
- Move the **Current status** header when a phase closes

**Record honest regressions.** If a change reduced coverage, dropped a capability, or deferred
something, say so in the tracker with the task id that owes the work. A metric that only ever
improves is a metric nobody trusts.

## The decision log is not optional

Any departure from the PRD goes in the decision log at the bottom of `.specs/PROGRESSO.md`,
with its reason. This includes:

- Dropping or deferring a task
- Choosing a different approach than the PRD specified
- A non-obvious constraint discovered while implementing

This file is the project's main portfolio artifact after the code itself. A dropped task with a
recorded rationale reads as judgement; a silently missing one reads as an unfinished project.

## Scope discipline

Do the task. Adjacent problems get **noted**, not fixed — either as a new tracker entry or a
line in the decision log. The roadmap has 80 tasks; drift is what stalls it.

The exception: if the task cannot be completed correctly without the adjacent fix, do both and
say so in the commit. Deleting `DotNetMcpServer.sln` (F1-06) required fixing
`FindRepositoryRoot` first, because it searched for `*.sln` — that is a genuine dependency, not
drift.

## Two things that are frozen

- **`src/Mcp.Protocol.Handwritten/`** — the study artifact. Correct it (F1-10), prove it
  interoperates (F1-11), then leave it alone. Extending it to new spec revisions is an explicit
  non-goal in PRD §5.
- **The SDK is the product path.** Do not reimplement what `ModelContextProtocol` provides.
  PRD §4 explains the reasoning; ADR-0001 carries it in the repository.
