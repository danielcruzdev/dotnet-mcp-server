---
name: write-adr
description: Use when recording an architectural decision for this project — choosing between the official SDK and hand-written code, a transport, a parser, a retrieval strategy, or any choice a reader would question. Also use when a roadmap task names an ADR (F1-14, F8-08) or when reversing an earlier decision.
---

# Writing an ADR

## Core principle

In this repository an ADR is not process paperwork. **It is the artifact that converts a
choice into evidence of judgement.** The project's central decision — shipping on the official
SDK while keeping a hand-written implementation as a study artifact — only reads as deliberate
because it is written down with its reasoning. Undocumented, the same repository reads as
someone who could not decide.

Write the ADR so it answers a question an interviewer would actually ask.

## Location and naming

`docs/adr/NNNN-kebab-case-title.md`, numbered sequentially from `0001`. Never renumber.

## Structure

```markdown
# NNNN — Title stating the decision, not the topic

- **Status:** Accepted | Superseded by [NNNN](NNNN-....md) | Reversed
- **Date:** YYYY-MM-DD

## Context
What forced a choice. Constraints that were real at the time — spec velocity, maintenance
cost, what the project is for. No solutions here.

## Decision
What was chosen, in one or two sentences, in the active voice.

## Consequences
What this buys and what it costs. **The costs are mandatory.** An ADR listing only benefits
is marketing, and a reader who spots the missing cost stops trusting the rest.

## Alternatives considered
Each real option, with the specific reason it lost. "Rejected as unsuitable" is not a reason.
```

## What makes these ADRs worth reading

- **Name the cost you accepted.** Choosing the SDK costs the "I built the protocol" claim.
  Say so. That admission is what makes the rest credible.
- **Quote the evidence.** Spec revisions with dates, benchmark numbers, an analyzer error, a
  failing test. A decision backed by a citation survives being questioned.
- **Reversals are content, not embarrassment.** PRD v1.0 argued the opposite of v2.0. An ADR
  that records what changed and why demonstrates judgement more convincingly than one that
  was right the first time. Mark the old one `Superseded by`, never delete it.
- **Short beats thorough.** One page. If it needs more, the decision is probably two decisions.

## ADRs this project owes

| # | Decision | Task |
|---|---|---|
| 0001 | Official SDK for the product, hand-written implementation as a frozen artifact | F1-14 |
| — | Streamable HTTP transport shape | F8-08 |
| — | Hand-written Pratt parser replacing `DataTable.Compute` | F8-08 |
| — | RAG retrieval strategy (hybrid vector + BM25) | F8-08 |

Source material for 0001 is already written: `.specs/PRD.md` §4 carries the reasoning and the
decision log in `.specs/PROGRESSO.md` records the reversal. The ADR condenses them — it does
not restate them at length.

## Common mistakes

- **Writing it after the fact as justification.** Write when deciding, while the alternatives
  are still live and the costs are still visible.
- **Omitting the alternative that nearly won.** That is the one a reader will ask about.
- **A title naming the topic instead of the decision.** "MCP SDK" says nothing;
  "Build on the official MCP SDK, keep the hand-written implementation as a study artifact"
  is the whole ADR in one line.
