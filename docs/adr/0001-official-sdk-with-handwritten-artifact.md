# 0001 — Build on the official MCP SDK, keep the hand-written implementation as a study artifact

- **Status:** Accepted
- **Date:** 2026-07-28

## Context

This repository started as a from-scratch implementation of the Model Context Protocol in
.NET: JSON-RPC framing, the stdio transport, the `initialize` handshake and the `tools/*`
methods, all written by hand. The stated reason was that hand-rolling the protocol
demonstrates depth a library cannot.

Two facts made that position untenable.

**The implementation did not interoperate.** It framed messages with an LSP-style
`Content-Length: N\r\n\r\n` header. The MCP specification requires the opposite:

> *"Messages are delimited by newlines, and MUST NOT contain embedded newlines."*
> — [MCP specification, stdio transport](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)

The server therefore connected to nothing except the agent in this same repository, which
spoke the same non-standard dialect. The two halves agreed with each other and with no one
else. For a project whose entire claim is protocol competence, that is disqualifying — and
discoverable by a reviewer in under a minute.

**The specification moves faster than one person can follow.** Four revisions shipped in
twenty months — `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25` — and the `2026-07-28`
release candidate landed the day this decision was made. The code targeted `2025-03-26`, two
revisions behind.

Meanwhile the [official C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) is
maintained in collaboration with Microsoft and classified
[Tier 1](https://modelcontextprotocol.io/docs/sdk) — the highest tier, shared only with
TypeScript, Python and Go, on the stated criteria of "feature completeness, protocol support,
and maintenance commitment."

## Decision

The shipped server and agent are built on the official `ModelContextProtocol` SDK.

The hand-written implementation stays in the repository at `src/Mcp.Protocol.Handwritten/`,
corrected to be spec-compliant, with its scope frozen and nothing shipped referencing it. It
is an executable, and CI drives it with the **official SDK client** as a real subprocess
(`HandwrittenServerInteropTests`).

## Consequences

**What this costs.** The repository can no longer claim "I built the protocol" about the code
that ships. That claim now attaches to an artifact explicitly labelled as not the product —
a weaker headline, and the honest one.

Maintaining a second implementation is also not free, even frozen. It is a project a reader
may mistake for indecision. The mitigation is this document plus the README ordering, and
neither is automatic.

**What this buys.** Spec revisions arrive as a version bump rather than a rewrite, and
`Directory.Packages.props` makes that bump a one-line change. Resources, prompts, completion,
progress and the Streamable HTTP transport come from the SDK, freeing roughly fourteen days of
roadmap that would have gone into re-deriving plumbing — redirected to tools, security
hardening, observability and retrieval, where the code is actually differentiated.

The artifact keeps the depth signal and, cross-validated against the official client, upgrades
it: interoperability is demonstrated rather than asserted.

**The effect on the argument.** "Why didn't you use the SDK?" is a question answered under
pressure. "I built it, proved it worked, then chose the SDK — here is the reasoning" is a
point made on one's own terms. The second is the stronger position, and it requires having
done both.

## Alternatives considered

**Ship the hand-written implementation, fix the framing.** Rejected. It keeps the strongest
headline but loses on every other axis: a permanent solo obligation to track a specification
that ships revisions two to three times a year, roughly 30% of the roadmap spent re-deriving
what the SDK provides, and a reviewer left to decide between two readings of the same
repository — "deep" or "did not research the ecosystem." An earlier revision of `.specs/PRD.md`
argued for this option; the reversal and its reasoning are recorded in the decision log of
`.specs/PROGRESSO.md`.

**Adopt the SDK and delete the hand-written code.** Rejected, though closer. It is clean and
removes the indecision risk entirely, but it discards the only evidence that the protocol was
ever understood rather than merely consumed, leaving a repository indistinguishable from any
tutorial that installed the package. The artifact is small, frozen and CI-verified; the
maintenance cost is bounded and the signal is not reproducible any other way.

**Use the SDK for the server and keep the hand-written client.** Rejected. A split that
follows no principle a reader could infer, so it communicates nothing. The hand-written client
also carried the worst defect in the codebase: a correlation loop that silently discarded any
response whose id did not match the pending request.
