---
name: writing-commits
description: Use when creating a git commit in this repository, when asked to commit, amend, or reword, and when deciding how to split a set of changes across commits.
---

# Writing commits

## The contract

A commit message is exactly these parts, in this order:

```
type(scope): imperative summary under 72 characters

Body. What changed and why it changed, wrapped at 72 columns. Blank line
between paragraphs. This is where the reasoning lives.

Trailers supplied by the harness, last.
```

**The blank line after the subject is load-bearing.** Git treats everything before the first
blank line as the subject. Omit it and the whole message becomes one subject — this repository
has a commit with a 3,598-character subject line for exactly that reason.

## Subject

`type(scope): summary`

| Type | For |
|---|---|
| `feat` | New capability |
| `fix` | Corrected behaviour |
| `refactor` | Structure changed, behaviour unchanged |
| `test` | Tests only |
| `docs` | Documentation only |
| `build` | Build, packaging, dependencies |
| `ci` | Pipeline configuration |
| `chore` | Tooling and repo housekeeping |

Scopes used here: `server`, `agent`, `artifact`, `tests`, `build`, `docs`, or `phase-N` for
roadmap work.

Imperative mood — "add", "replace", "remove", not "added" or "adds". No trailing period.
Lowercase after the colon.

## Body

The body answers three questions. The diff already lists the files; do not restate them.

1. **What changed**, at the level of behaviour, not file names.
2. **Why** — the constraint, bug, or decision that forced it. This is the part that is
   expensive to reconstruct a year later and impossible to recover from the diff.
3. **What is not done** — a regression accepted, a follow-up owed, something deliberately
   left out. A commit that only reports wins trains readers to skim.

For roadmap work, name the task ids and audit findings the commit closes (`Closes F1-10`,
`Resolves C1, A12`). The subject stays conventional; ids live in the body, where they can be
listed and explained.

## Granularity

One logical change per commit. If the body needs "and also", it is two commits.

Separating them is not bookkeeping — `1b5a8f1` in this repository bundles a bug fix, a feature
and a docs rewrite, so none of the three can be reverted, reviewed, or found by `git log`
independently.

Planning and implementation are separate commits: the decision and the execution get
questioned separately.

## Writing the message

Use a heredoc so quoting and encoding stay predictable:

```bash
git commit -F - <<'EOF'
fix(agent): resolve the repository root from any working directory

The MCP server failed to start when the agent was launched from an IDE with
an empty working-directory setting: workingDirectory "." resolved to the
agent's own folder rather than the repository root.

Root detection now walks up looking for a solution file or a .git directory.

Closes F1-06.
EOF
```

`-F -` avoids the byte-order mark that a GUI editor put at the front of `1b5a8f1`'s subject,
where it is invisible and permanent.

## Before committing

Build clean and tests green — see the `verify-mcp-server` skill for anything touching the
protocol. A commit is a claim that the tree works at that point.

## Reference

The four most recent commits in this repository follow this contract. `git log 141874f..HEAD`
shows the shape; the five commits before them show what it replaced.
