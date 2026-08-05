# Execution Tracker — DotNetMcpServer

> Companion to [`PRD.md`](PRD.md) v2.0. Task IDs match one-to-one and are named in commit bodies.
> **Last updated:** 2026-08-04

---

## Current status

**Phase 3 — Full MCP Surface via SDK** ✅ complete · **Phase 4 — Streamable HTTP + OAuth 2.1** ⬜ next

```
Overall   ████████░░░░░░░░░░░░  33 / 80 tasks   (41%)

Phase 1   ████████████████████  14 / 14   ✅ complete
Phase 2   ████████████████████   9 / 9   ✅ complete
Phase 3   ████████████████████  10 / 10   ✅ complete
Phase 4   ░░░░░░░░░░░░░░░░░░░░   0 / 8
Phase 5   ░░░░░░░░░░░░░░░░░░░░   0 / 11
Phase 6   ░░░░░░░░░░░░░░░░░░░░   0 / 10
Phase 7   ░░░░░░░░░░░░░░░░░░░░   0 / 7
Phase 8   ░░░░░░░░░░░░░░░░░░░░   0 / 11
```

**Next action:** Phase 4 — `F4-01`, `ModelContextProtocol.AspNetCore` and Streamable HTTP.
Phase 5 is also unblocked (it only needs Phase 2) and is the one the SDK contributes nothing
to, so it carries more signal per hour than Phase 4 does.

**Phase 3 outcome:** the server serves every MCP capability, not just tools — resources with
templates and subscriptions, prompts, completion, logging, progress, structured output,
elicitation and sampling, each driven by the official client against the real binary. The
finding that mattered most was not in the task list: **the spec revision the SDK negotiates by
default has already removed `resources/subscribe` and `logging/setLevel`, and deprecated
Sampling, Logging and Roots.** Two tasks were half-obsolete before they were written. Both
were found by an interop test failing, six days after the audit that argued spec velocity was
the reason to build on the SDK — see the decision log.

**Phase 2 outcome:** the agent is a hosted, injected, validated, observable application, and
every project reports coverage — **70.1%**, against a 60% bar. The two findings that mattered
most were not in the task list: a rate limit used to end the turn, and a closed stdin spun a
core. Both were found by running the thing rather than by reading it.

**Phase 1 outcome:** both servers are driven end to end by the **official SDK client** as real
subprocesses — 13 integration tests. The hand-written artifact interoperates with an
independent implementation, which turns the project's central claim from an assertion into
evidence. C1, C3, C4, A1, A2, A3 and A12 are closed.

---

## Legend

| Mark | Meaning |
|:---:|---|
| ⬜ | Not started |
| 🟦 | In progress |
| ✅ | Done and verified |
| ⏸️ | Paused — see the decision log |
| ❌ | Dropped — see the decision log |

The **Fixes** column links each task back to an audit finding in [`PRD.md` §3](PRD.md#3-current-state-audit).

---

## Phase 1 — SDK Migration & Foundation 🔴

**Goal:** the server connects to Claude Desktop, and the hand-written implementation becomes a correct, documented artifact.
**Blocks:** everything. **Effort:** 3–4 days.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ✅ | **F1-01** | Add `!.env.example` to `.gitignore` and commit the file | C2 |
| ✅ | **F1-02** | Remove the `apiKey` field from `appsettings.json` — env var only | S5 |
| ✅ | **F1-03** | Add `Directory.Build.props` (shared TFM, `TreatWarningsAsErrors`, analyzers, deterministic builds) | B1 |
| ✅ | **F1-04** | Add `Directory.Packages.props` — makes SDK version bumps a one-line change | B2 |
| ✅ | **F1-05** | Add `.gitattributes` with `* text=auto eol=lf` | B4 |
| ✅ | **F1-06** | Delete `DotNetMcpServer.sln`; keep `.slnx` only | B3 |
| ✅ | **F1-07** | Migrate the server to `ModelContextProtocol`: host builder, DI-registered tools, stdio transport | C1, C3, C4, A3 |
| ✅ | **F1-08** | Migrate the agent's MCP client to `ModelContextProtocol.Core` | A1, A2 |
| ✅ | **F1-09** | Relocate the hand-written implementation to `src/Mcp.Protocol.Handwritten/`, scope documented as frozen | — |
| ✅ | **F1-10** | Correct the artifact: newline-delimited framing via `System.IO.Pipelines` + `Utf8JsonReader` | C1, A12 |
| ✅ | **F1-11** | **Cross-validate the artifact against the official SDK client in CI** | C1, C3, C4 |
| ✅ | **F1-12** | Launch the compiled binary instead of `dotnet run` | C5 |
| ✅ | **F1-13** | Verify in Claude Desktop / VS Code / Claude Code; write `docs/INSTALL.md` | D1 |
| ✅ | **F1-14** | **Write ADR-0001** — hand-written vs official SDK, and why the artifact stays | D2 |

**Done when**
- [x] **A CI test drives the shipped server with the official SDK client** — handshake, `tools/list`, 3 tool calls, 1 rejected traversal (`SdkServerInteropTests`, 6 tests)
- [x] **The same harness drives the hand-written artifact** — handshake, version negotiation, tool discovery, tool calls, numeric round-trip, embedded-newline survival, unknown-tool error (`HandwrittenServerInteropTests`, 7 tests)
- [x] `dotnet build` emits zero warnings with `TreatWarningsAsErrors` on, in Debug **and** Release
- [x] `git ls-files` includes `.env.example`
- [x] ADR-0001 is committed and answers the question without hedging
- [x] `docs/INSTALL.md` documents Claude Code, Claude Desktop and VS Code against the compiled binary
- [ ] **Claude Desktop visually confirmed by a human** — the binary is proven to serve MCP by the
      interop suite, which is what Claude Desktop does, but nobody has watched the desktop app
      connect. Owner: Daniel. Blocks nothing; evidence for the `F8-07` demo recording.

**Carried into later phases** — deliberate, not forgotten:
- ~~`ScenarioTests` (18 cases) and `WorkspaceFixture` were deleted with the `IMcpTool` abstraction they tested.~~ **Settled in Phase 2.** `AgentTurnTests` rebuilt the multi-tool scenarios on the interop harness under `F2-07`, and `F2-08` added the tool-behaviour coverage the old suite never had.

---

## Phase 2 — Modern .NET Architecture 🟠

**Goal:** the code reads like a senior .NET engineer wrote it.
**Depends on:** Phase 1. **Effort:** 3–4 days.

> The SDK pushes the *server* toward DI for free. The **agent** gets none of that — and most of these findings live there.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ✅ | **F2-01** | Adopt the Generic Host in the agent, matching the server's SDK-provided host | A6 |
| ✅ | **F2-02** | `IOptions<T>` + `ValidateDataAnnotations().ValidateOnStart()` | A6 |
| ✅ | **F2-03** | Structured `ILogger` — server logs to stderr only | A6, C5 |
| ✅ | **F2-04** | `IHttpClientFactory` + resilience: retry, backoff, timeout, circuit breaker, 429 | A8 |
| ✅ | **F2-05** | Move directory creation out of `AppendStudyNoteTool`'s constructor | B6 |
| ✅ | **F2-06** | Make console input cancellable (Ctrl+C works) | A9 |
| ✅ | **F2-07** | Degrade gracefully at the tool-iteration limit | A10 |
| ✅ | **F2-08** | Reference and test the Agent project | A5 |
| ✅ | **F2-09** | Migrate all comments, messages, and logs to English | D3 |

**Done when**
- [x] No `new` on a service type in either entry point — the agent's `Program.cs` is a host
      builder; `HttpClient`, the chat client, the runner and the MCP connection all come from DI
- [x] A missing `OPENAI_API_KEY` fails at startup with a clear message — verified by running
      the binary with the variable unset: `DataAnnotation validation failed for
      'OpenAiSettings' members: 'ApiKey'`, exit code 82
- [x] A transient HTTP 429 is retried transparently, proven by a stub-handler test — four tests
      in `OpenAiResilienceTests` drive the shipped registration with only the transport
      replaced: a 429 then a 200 succeeds in two attempts, a permanent 429 stops at four, and a
      400 is not retried at all
- [x] Coverage ≥ 60% with every project reporting — **70.1% line, 57.2% branch**. Agent 68.0%,
      Server 82.2%, artifact 66.9%. Understated: everything the interop suite runs lives in a
      subprocess the collector does not instrument, so all three `Program` entry points and the
      artifact's conformance tools read as 0% while being exercised on every run
- [x] No Portuguese strings remain in `src/` — swept across all 30 tracked files: comments,
      XML docs, exception text, log messages, console output and `appsettings.json`. Also
      clean in `tests/` and `docs/`

**What the ✅s do and do not mean:**
- `F2-08` closed at 70.1%, but read the shape rather than the number. `AgentHostedService` is
  now driven for real — started against a server subprocess, stopped while its session is
  parked on input. What stays uncovered in-process is the three `Program` entry points and the
  artifact's conformance tools, all of which run on every interop test, in another process.
  The measurement is conservative, not generous.
- `F2-05` closed against a finding that had already half-expired. B6 described a
  `Directory.CreateDirectory` call in `AppendStudyNoteTool`'s **constructor** — but Phase 1
  deleted that class along with the `IMcpTool` abstraction, so by the time the task came up
  the side effect was already at call time. What was left was the second half of the task's
  wording, moving the creation into the injected workspace service, which is done. Recorded
  because "✅" here means less than the finding text implies.
- `F2-09` closed on a sweep that found nothing left to migrate. The work had already happened
  incidentally: Phase 1 rewrote most of `src/` onto the SDK, and rewritten files came back in
  English. The task's value was the verification, not the change — worth recording so the ✅ is
  not read as a day's translation.

---

## Phase 3 — Full MCP Surface via SDK 🟠

**Goal:** serve every MCP capability, not just tools.
**Depends on:** Phase 2. **Effort:** 3–4 days.

> On the SDK this is handler work, not protocol work. v1.0 budgeted 5–6 days here; v2.0 budgets 3–4 **and adds two features** — elicitation and sampling. That gap is the SDK decision paying for itself.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ✅ | **F3-01** | `resources/list` + `resources/read` | A4 |
| ✅ | **F3-02** | Resource templates (RFC 6570 URI templates) | A4 |
| ✅ | **F3-03** | `resources/subscribe` + update notifications via `FileSystemWatcher` | A4 |
| ✅ | **F3-04** | `prompts/list` + `prompts/get` with arguments | A4 |
| ✅ | **F3-05** | `completion/complete` for prompt and resource arguments | A4 |
| ✅ | **F3-06** | `logging/setLevel` + log notifications | A4 |
| ✅ | **F3-07** | Progress notifications for long-running tools | A4 |
| ✅ | **F3-08** | `outputSchema` + structured content + tool annotations | A4 |
| ✅ | **F3-09** | **Elicitation** — tools can ask the user for missing input mid-execution | — |
| ✅ | **F3-10** | **Sampling** — the server can request an LLM completion from the client | — |

**Done when**
- [ ] **Claude Desktop lists resources and prompts alongside tools** — the interop suite proves
      a real client lists both, but nobody has watched the desktop app do it. Owner: Daniel.
      Same standing item as Phase 1's visual confirmation; evidence for the `F8-07` recording
- [x] **Editing a workspace file triggers an update notification the client receives** — proven
      on `2025-11-25`, the last revision that has `resources/subscribe`. On `2026-07-28` the
      method is gone and per-resource updates do not flow; `notifications/resources/list_changed`
      works on both. See the decision log entry for 2026-08-03
- [x] **A long-running tool reports progress visible in the client** — `scan_workspace`, one
      report per document, asserted over a real client
- [x] **An elicitation round-trip completes against a real client** — `append_study_note`
      called without a title asks for one, and the answer lands in the note on disk
- [x] **Every advertised capability is exercised by a test** — resources (list, read,
      templates, subscribe, list-changed), prompts, completion, logging, progress, structured
      output, elicitation and sampling each have interop cases driving the real binary

**What the ✅s do and do not mean:**
- The phase closed with **two capabilities that the current protocol revision has already
  moved on from**. `resources/subscribe` and `logging/setLevel` were removed by `2026-07-28`,
  and Sampling, Logging and Roots are all deprecated by it. Everything works against the
  revision shipping clients negotiate, and two tests pin each boundary so it cannot move
  quietly. This is not a footnote on the phase — it is the phase's most useful finding, and
  the clearest evidence PRD §4's spec-velocity argument was the right one.
- `F3-03` is ✅ for per-resource updates on `2025-11-25` and earlier only. On `2026-07-28` the
  SDK owns the subscription list privately and offers no server-side hook, so a server cannot
  learn which URIs a client follows. Owed work, not hidden work: revisit when the SDK client
  gains a `subscriptions/listen` API.

---

## Phase 4 — Streamable HTTP + OAuth 2.1 🔵

**Goal:** a genuinely remote, authenticated, deployable server.
**Depends on:** Phase 3. **Effort:** 5–6 days.

> The SDK provides the transport. **The authorization work is entirely yours** — and that is the part worth demonstrating.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ⬜ | **F4-01** | Add `ModelContextProtocol.AspNetCore`; expose MCP over Streamable HTTP | — |
| ⬜ | **F4-02** | Session management and SSE streaming verified end to end | — |
| ⬜ | **F4-03** | Resumability: event IDs + `Last-Event-ID` replay | — |
| ⬜ | **F4-04** | Validate the `Origin` header and bind to localhost by default | S6 |
| ⬜ | **F4-05** | OAuth 2.1 resource server: RFC 9728 metadata, `WWW-Authenticate`, JWT validation | — |
| ⬜ | **F4-06** | Per-tool authorization scopes enforced before dispatch | S6 |
| ⬜ | **F4-07** | Multi-stage `Dockerfile` + `docker-compose.yml` with a local identity provider | D5 |
| ⬜ | **F4-08** | Run the full suite against **both** transports from one parameterized test | — |

**Done when**
- [ ] The suite runs green against both transports from a single test theory
- [ ] A missing or invalid token returns 401 with a correct `WWW-Authenticate` header
- [ ] A forged `Origin` is rejected
- [ ] A dropped SSE connection resumes from `Last-Event-ID` with no message loss
- [ ] `docker compose up` yields a working server

---

## Phase 5 — Tool Library & Security Hardening 🟡

**Goal:** tools worth pointing a real agent at, behind a boundary that actually holds.
**Depends on:** Phase 2. **Effort:** 5–6 days.

> **The SDK contributes nothing here.** Every line is yours — which is exactly why this phase carries more signal than the protocol work it replaced.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ⬜ | **F5-01** | `list_directory` — paginated, glob-filtered | D4 |
| ⬜ | **F5-02** | `write_text_file` — guarded writes with dry-run | D4 |
| ⬜ | **F5-03** | `search_files` — regex/glob content search with context lines | D4 |
| ⬜ | **F5-04** | `git_log` + `git_diff` — read-only | D4 |
| ⬜ | **F5-05** | `http_fetch` with domain allowlist, size cap, and SSRF protections | D4 |
| ⬜ | **F5-06** | Fix path comparison: case-sensitive on Linux/macOS, insensitive on Windows | S1 |
| ⬜ | **F5-07** | Resolve symlinks; reject any link escaping the workspace | S2 |
| ⬜ | **F5-08** | Stream reads with a byte cap; guard surrogate pairs on truncation | S3 |
| ⬜ | **F5-09** | Deny-list `.git/`, `.env*`, `*.pem`, `id_rsa*`, `*.pfx` | S6 |
| ⬜ | **F5-10** | Per-tool timeouts and rate limiting | S6 |
| ⬜ | **F5-11** | Replace `DataTable.Compute` with a hand-written Pratt parser | S4 |

**Done when**
- [ ] A symlink inside the workspace pointing outside it is rejected, proven by a test
- [ ] Path-traversal tests pass on all three CI platforms, case-sensitivity included
- [ ] Reading a 1 GB file respects the character cap without loading it into memory
- [ ] The expression parser has property-based tests and no `System.Data` dependency

---

## Phase 6 — Agent Intelligence & Observability 🔵

**Goal:** see inside the agent, and make it good enough to be worth watching.
**Depends on:** Phase 2 (Phase 5 recommended). **Effort:** 6–7 days.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ⬜ | **F6-01** | Adopt `Microsoft.Extensions.AI` `IChatClient` | A11 |
| ⬜ | **F6-02** | Pluggable providers: OpenAI, Azure OpenAI, Anthropic, Ollama | A11 |
| ⬜ | **F6-03** | Token-by-token streaming in the console | A11 |
| ⬜ | **F6-04** | Context-window management: sliding window + summarization | A7 |
| ⬜ | **F6-05** | Token and cost accounting per turn and session | A11 |
| ⬜ | **F6-06** | OpenTelemetry traces spanning agent → LLM → MCP → tool | — |
| ⬜ | **F6-07** | Metrics: tool latency, call counts, error rates, token throughput | — |
| ⬜ | **F6-08** | BenchmarkDotNet: hand-written transport vs the SDK's — a result only the artifact makes possible | A12 |
| ⬜ | **F6-09** | Source-generated JSON serialization | — |
| ⬜ | **F6-10** | Native AOT publish + Aspire Dashboard in `docker-compose` | — |

**Done when**
- [ ] One trace shows the full path from user prompt to tool execution and back
- [ ] Benchmarks are committed with numbers in the README
- [ ] The AOT server starts in under 50 ms and passes the full suite
- [ ] A 100-turn conversation stays within the context window

> Order matters: **F6-09 before F6-10.** Reflection-based serialization breaks under AOT.

---

## Phase 7 — RAG & Vector Memory 🟣

**Goal:** the agent remembers, and answers from a corpus larger than its context window.
**Depends on:** Phases 5 and 6. **Effort:** 7–9 days.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ⬜ | **F7-01** | `IEmbeddingGenerator` with a local model option | — |
| ⬜ | **F7-02** | Vector store: SQLite-vec on disk, in-memory for tests | — |
| ⬜ | **F7-03** | Incremental ingestion: workspace → chunking → embedding → index | — |
| ⬜ | **F7-04** | `search_knowledge` tool with score thresholds and source citations | D4 |
| ⬜ | **F7-05** | Hybrid retrieval: vector + BM25 with reciprocal-rank fusion | — |
| ⬜ | **F7-06** | Persistent conversation memory scoped per workspace | A7 |
| ⬜ | **F7-07** | Retrieval evaluation harness with a fixed question set | — |

**Done when**
- [ ] Indexing `examples/workspace/` and asking a question surfaces the right document with a citation
- [ ] The evaluation harness reports recall@5 with a committed baseline
- [ ] The full RAG demo runs offline with a local embedding model
- [ ] Restarting the agent preserves prior conversation memory

---

## Phase 8 — Distribution & Showcase ⚪

**Goal:** the work becomes visible and installable — where a portfolio project pays off.
**Depends on:** Phases 4 and 7. **Effort:** 4–5 days.

| | ID | Task | Fixes |
|:---:|---|---|---|
| ⬜ | **F8-01** | Package the server as a `dotnet tool` | D5 |
| ⬜ | **F8-02** | CI matrix: ubuntu + windows + macos | B5 |
| ⬜ | **F8-03** | Enforce `dotnet format --verify-no-changes` in CI | B5 |
| ⬜ | **F8-04** | Collect coverage → Codecov → badge | B5 |
| ⬜ | **F8-05** | Release workflow: semver, GitHub Releases, signed artifacts, SBOM | D5 |
| ⬜ | **F8-06** | English README **and `examples/EXAMPLES.md`**: demo GIF, diagrams, install snippet, badges — SDK path first, artifact as "how it works underneath" | D1, D3 |
| ⬜ | **F8-07** | Record the demos: agent session, Claude Desktop, distributed trace | D1 |
| ⬜ | **F8-08** | Remaining ADRs: Streamable HTTP, Pratt parser, RAG retrieval strategy | D2 |
| ⬜ | **F8-09** | Add `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md` | D2 |
| ⬜ | **F8-10** | Enable Dependabot and CodeQL — this is what keeps the SDK current for free | B5 |
| ⬜ | **F8-11** | Publish a DocFX site to GitHub Pages | D2 |

**Done when**
- [ ] `dotnet tool install -g` works on a clean machine and connects to Claude Desktop
- [ ] The README opens with a GIF of a real agent session
- [ ] CI is green on all three platforms with coverage above 80%
- [ ] Every ADR answers a question an interviewer would plausibly ask

---

## Metrics

Update after each phase. Baseline measured 2026-07-28 at commit `1b5a8f1`.

| Metric | Baseline | Current | Target |
|---|---|---|---|
| Connects to a real MCP client | ❌ No | ✅ **Yes** — SDK client, real subprocess | ✅ Yes |
| Protocol revision | `2025-03-26` | negotiated by the SDK | current stable, via SDK |
| Artifact interoperates with the SDK client | ❌ No | ✅ **Yes** — 7 tests in CI | ✅ Verified in CI |
| MCP capabilities served | tools only | **tools + resources + prompts + completion + logging + progress + elicitation + sampling** | tools + resources + prompts + logging + completion |
| MCP tools exposed | 4 | 5 | 12+ |
| Test cases | 69 | 159 | 200+ |
| Integration tests (real client ↔ real server) | 0 | **84** | grows with each phase |
| Line coverage | not measured | **70.1%** (branch 57.2%) | ≥ 80% |
| Projects under test | 2 / 3 | **4 / 4** | all |
| Build warnings | not enforced | **0, enforced** | 0, enforced |
| CI platforms | 1 | 1 | 3 |
| ADRs published | 0 | **1** | 4+ |
| Transports | 1 (non-compliant) | 1 (spec-compliant, both servers) | 2 (stdio + HTTP) |

> **The scenario coverage owed since Phase 1 is rebuilt, on better ground than it stood on before.**
> Deleting `ScenarioTests` removed 18 in-process cases against the old `IMcpTool` abstraction. The
> replacement is `AgentTurnTests`: the agent's real turn loop, driving real tools on a real server
> subprocess, with only the model stubbed. Three cases rather than eighteen, but they cross the
> process boundary the old ones never touched. Line coverage is still unmeasured — that is what
> `F2-08` has left.

---

## Audit findings coverage

Every finding in [`PRD.md` §3](PRD.md#3-current-state-audit) is claimed by at least one task.

| Severity | Findings | Resolved by |
|---|---|---|
| 🔴 Critical | C1–C5 | Phase 1 |
| 🟠 Architecture | A1–A3, A12 | Phase 1 — absorbed by the SDK migration |
| 🟠 Architecture | A5, A6, A8–A10 | Phase 2 |
| 🟠 Architecture | A4 | Phase 3 |
| 🟠 Architecture | A7, A11 | Phases 6–7 |
| 🟡 Security | S5 | Phase 1 |
| 🟡 Security | S1–S4, S6 | Phases 4–5 |
| 🔵 Build & CI | B1–B4 | Phase 1 |
| 🔵 Build & CI | B6 | Phase 2 |
| 🔵 Build & CI | B5 | Phase 8 |
| ⚪ Docs & product | D3 | Phase 2 |
| ⚪ Docs & product | D4 | Phases 5, 7 |
| ⚪ Docs & product | D1, D2, D5 | Phases 1, 4, 8 |

---

## Decision log

Record every deviation from the PRD here, with the reason. This is the file that makes the roadmap defensible six months from now.

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-28 | **Reversed: build on the official SDK, not the hand-written protocol** (PRD v1.0 → v2.0) | Three flaws in the original reasoning. (1) It optimized for differentiation over judgment — knowing what *not* to build is the scarcer hiring signal. (2) It under-weighted spec velocity: four revisions in twenty months, and the `2026-07-28` RC landed the day of the audit. Tier 1 means the SDK tracks that; hand-rolled means one person tracks it alone, forever. (3) It under-weighted opportunity cost — ~14 days re-deriving resources, prompts, and Streamable HTTP, roughly 30% of the roadmap on its least differentiated layer. |
| 2026-07-28 | Keep the hand-written implementation as a scoped, frozen study artifact | It still carries the depth signal, and cross-validating it against the official SDK client (`F1-11`) converts that from a claim into evidence. Deleting it would throw away the only thing that makes the judgment story credible. |
| 2026-07-28 | Do not publish the hand-written implementation to NuGet | Shipping a redundant protocol library would undercut the exact judgment ADR-0001 argues for. |
| 2026-07-28 | Migrate the entire codebase to English | Wider reach and alignment with open-source convention. |
| 2026-07-28 | C5 (`dotnet run` writing to stdout) survives the SDK migration | The SDK owns framing, not process launch. MSBuild output still lands on the protocol channel if the server is started with `dotnet run`. Fixed in `F1-12`. |
| 2026-07-28 | F6-09 (source-gen JSON) must precede F6-10 (Native AOT) | Reflection-based serialization fails under AOT trimming. |
| 2026-07-28 | `AnalysisLevel` set to `latest-recommended`, not `latest-all` as the PRD specified | `latest-all` enables every CA rule including opinionated ones that fight ordinary code. `latest-recommended` plus `TreatWarningsAsErrors` already yields a zero-warning build and caught four real issues on the first run. Revisit if the bar needs raising. |
| 2026-07-28 | CA1707 suppressed in `tests/` (plus CA1001, CA1844 for stream doubles) | CA1707 forbids underscores in member names — it targets public API surface. Underscore-separated test names are the established .NET convention and are what makes CI output readable. |
| 2026-07-28 | Deleting `DotNetMcpServer.sln` would have silently broken the agent | `AgentSettingsLoader.FindRepositoryRoot` searched for `*.sln` specifically — removing the file reintroduces the exact bug commit `1b5a8f1` fixed. Root detection now accepts `*.slnx`, `*.sln`, or `.git`. |
| 2026-07-28 | `InvariantGlobalization` pinned to `false` with a comment explaining why | It was switched on as a reflex during F1-03 and broke `get_current_datetime`: it disables ICU, so IANA ids like `America/Sao_Paulo` stop resolving. The comment exists so nobody re-enables it. |
| 2026-07-28 | `CallToolResult.IsError` is `bool?`, and `null` means success | Not `false`. Assertions must be `Assert.NotEqual(true, result.IsError)`; `Assert.False` fails against `null`. Worth knowing before writing any further interop test. |
| 2026-08-02 | **`F2-01`, `F2-02` and `F2-03` landed as one commit, not three** | They are one physical change. Adopting the Generic Host forces a decision on configuration binding and on logging in the same movement; sequencing them would have meant writing wiring that the next task deletes. The commit body names all three ids. |
| 2026-08-02 | Flat environment variable names kept (`OPENAI_API_KEY`), instead of the framework's `Section__Key` convention | The `__` convention needs no translation layer, but `.env.example` and `docs/INSTALL.md` document the flat names and `OPENAI_API_KEY` is an ecosystem convention rather than something this project invented. `FlatEnvironmentVariables` maps them onto configuration keys — about twenty lines, and the documented contract survives. |
| 2026-08-02 | **`Math.Clamp` on `MaxToolIterations` replaced by `[Range(1,12)]` — a behaviour change** | An out-of-range value used to be silently corrected into range. Correcting a user's configuration without telling them hides the mistake; it now fails at startup. Observable change: `AGENT_MAX_TOOL_ITERATIONS=99` previously ran with 12 and now refuses to start. |
| 2026-08-02 | `AgentSettingsLoader` deleted; path derivation moved to `McpSettingsSetup` (`IPostConfigureOptions<McpSettings>`) | `IConfiguration` replaces the JSON loading and environment overrides, but repository-root discovery and server-binary resolution are post-binding derivation, not binding. As an options setup they are constructor-injectable and unit-testable — the logic had no tests at all before. CLAUDE.md, `docs/INSTALL.md` and the `verify-mcp-server` skill named the old symbol and were updated in the same commit. |
| 2026-08-02 | **`.env.example` reintroduced C5 and was fixed outside its task** | It shipped `MCP_COMMAND=dotnet` and `MCP_ARGUMENTS=run --project …`. `appsettings.json` deliberately leaves both blank so the compiled binary is resolved, but environment variables override the blank — so anyone following the documented example got `dotnet run` back, putting MSBuild output on the protocol channel. Adjacent to `F2-02` rather than part of it, fixed because the file documents the exact mechanism this task rewrote. |
| 2026-08-02 | Analyzers force `LoggerMessage` source generation, not direct `ILogger` calls | CA1848 and CA1873 are errors under `TreatWarningsAsErrors`. This pushed the logging toward source-generated partial methods, which is the idiomatic form anyway — worth knowing before writing any further logging in this repository. |
| 2026-08-02 | **`AddStandardResilienceHandler` adopted, but its timeouts are wrong for this caller and were widened** | The standard pipeline defaults to 10 s per attempt and 30 s overall — sized for a fast internal dependency. A chat completion carrying tool definitions routinely exceeds both, so shipping the defaults would have introduced a timeout bug the previous raw `HttpClient` did not have. Raised to 100 s per attempt and 300 s overall. The circuit breaker's sampling duration follows at 240 s because the library validates it at ≥ 2× the attempt timeout. |
| 2026-08-02 | The circuit breaker's default threshold is unreachable for this agent, so it was lowered | 100 requests per sampling window with a 10% failure ratio describes a service under load, not a single-user console session — the breaker would never have opened, making it decoration. Set to 4 requests at a 50% ratio: high enough that one blip does not trip it, low enough that a sustained outage fails fast. |
| 2026-08-02 | `HttpClient.Timeout` set to `Timeout.InfiniteTimeSpan` on the typed client | `HttpClient.Timeout` sits above the handler chain and bounds the *entire* send, retries included. Left at its 100 s default it would have silently capped the 300 s total-request policy and cancelled retries mid-flight. The resilience pipeline owns every timeout, or none of them. |
| 2026-08-02 | Runtime extension packages moved 10.0.0 → 10.0.10, forced by the resilience package | `Microsoft.Extensions.Http.Resilience` ships from dotnet/extensions on its own version train (10.8.0) and requires `Microsoft.Extensions.Http` ≥ 10.0.10; the 10.0.0 pin produced NU1605, which is an error here. `Hosting` and `Options.DataAnnotations` were moved with it to keep the servicing train consistent rather than leaving one package ahead. |
| 2026-08-02 | **B6 was already half-fixed by the Phase 1 migration; `F2-05` closed the rest** | The finding named a `Directory.CreateDirectory` call in `AppendStudyNoteTool`'s constructor. That class no longer exists — Phase 1 replaced it with a static `[McpServerTool]` method — so the constructor side effect went away with the constructor. The remaining half was real: the tool still called `Directory.CreateDirectory` itself, one step outside the containment guard. It moved to `WorkspaceContext.EnsureDirectory`, so a directory can only be created inside the workspace. Findings written against a pre-migration tree need re-reading before they are worked, not just ticking. |
| 2026-08-02 | **A9 overstated its symptom, and the real defect was worse than the stated one** | The finding said Ctrl+C "sets the token but the process stays blocked on the read". Measured against the pre-change build with a real `CTRL_C_EVENT`: the process exits cleanly, in 5.1 s, exactly as it does after the fix. The Generic Host tears the process down whether or not the session cooperates. What was actually broken is that the session was *abandoned* mid-read rather than unwound — harmless for an agent with nothing to flush, and silently lossy the moment it has any. `StopAsync` now awaits the session, bounded by the host's shutdown timeout. |
| 2026-08-02 | **A hot loop on stdin EOF was fixed alongside `F2-06`, outside the task** | `Console.ReadLine()` returns `null` at end of input, and the loop treated that like an empty line and `continue`d. Measured on the committed build: one core saturated and 433 KB of `You > ` prompts written to stdout in five seconds. It lives in the exact loop `F2-06` rewrites, so leaving it would have meant shipping a known spin through the lines being changed. End of input now breaks the loop like `exit` does. |
| 2026-08-02 | Agent shutdown takes ~5 s, cause not identified — recorded rather than guessed at | Measured identically before (5.11 s) and after (5.08 s) `F2-06`, so it is not a regression from that work. Ruled out: the server, which exits on stdin EOF in 0.32 s. Not chased further because it is outside the task and costs nothing but patience at the prompt. Whoever touches agent shutdown next should start here. |
| 2026-08-02 | `IUserInput` introduced as a one-method seam rather than reading the console inline | Two concrete reasons, neither speculative: the cancellable-read behaviour needs a test with a reader that genuinely blocks, and `F2-08` owes coverage of `InteractiveAgentRunner`, which cannot be driven at all while it calls `Console.ReadLine` directly. The implementation is two lines — `Task.Run` for the read, `WaitAsync` for the cancellation. |
| 2026-08-02 | The agent's turn loop is tested against a **real** MCP server, not a faked client | `McpClient` is abstract with non-virtual methods and an `[Experimental]` constructor, so a test double is not available without suppressing a diagnostic that `TreatWarningsAsErrors` turns into a build failure. Rather than fight that, `AgentTurnTests` stubs only the model — over the same `HttpMessageHandler` seam `F2-04` established — and lets the tool calls really execute against the server subprocess. The constraint produced a better test than the one originally intended. |
| 2026-08-02 | `InternalsVisibleTo` added to the Agent project instead of widening its public API | `CompleteTurnAsync` needs to be reachable from a test, and it is not something a consumer should call. One MSBuild item is cheaper than a public method that exists only for testing, and it is what makes the `F2-08` coverage work possible without further surface changes. |
| 2026-08-02 | The exhausted-turn answer is the model's own narration plus a notice — no extra model call | The alternative was one final completion with tools disabled, forcing an answer. That reads better but adds an API call, a failure mode, and a second timeout to the unhappy path. The narration the model already produced is a real partial answer and costs nothing. Revisit if it proves too thin in use. |
| 2026-08-02 | **`examples/EXAMPLES.md` is still Portuguese and belonged to no task; folded into `F8-06`** | Found by the `F2-09` sweep. `F2-09`'s criterion is `src/`, and D1 hands the README to `F8-06`, but nothing claimed the examples document — it would have shipped in Portuguese next to an English README. It is not translated here because it should be rewritten with the README, in one voice, not patched ahead of it. |
| 2026-08-02 | **The first coverage run reported 58.5% and the number was wrong** | Stale assemblies from before the project rename — `PortfolioAgent.Tests.dll` and `PortfolioAgent.Shared.dll`, built in March — were still sitting in `bin/` and the collector instrumented them at 0%. Deleting `bin/` and `obj/` and re-measuring gave 60.1% on identical code. Two lessons kept here: coverage was about to be reported 1.6 points low against a 60% bar, and a clean build is part of measuring anything. `F8-04` should clean before collecting in CI. |
| 2026-08-02 | Line coverage understates this repository, and the gap is structural | Everything the interop suite exercises runs in a subprocess the collector does not instrument, so all three `Program` entry points and the artifact's conformance tools read as 0% while executing on every run. Chasing them with in-process tests would mean duplicating the interop suite badly. The 70.1% figure is conservative, and the ≥ 80% target in `F8-04` should be set against that, not against a hypothetical instrumented-subprocess number. |
| 2026-08-02 | `F2-08`'s coverage work was spent on real gaps, not on the percentage | The tools' own contract had nothing behind it: the character cap, the out-of-range clamp, the missing-file message and append-versus-overwrite were only smoke-tested through interop. Those tests moved the Server package from 65.0% to 82.2% as a side effect. `AgentHostedService` was added for the same reason — it owns the shutdown path `F2-06` changed and had no test at all. |
| 2026-08-03 | **`F3-01` and `F3-02` landed as one commit** | The template reuses the containment and file access the listing added, in the same file. Split, the first commit would ship a provider with a method nothing calls. The commit body names both ids. |
| 2026-08-03 | **The spec deleted `resources/subscribe` while `F3-03` was being written** | The `2026-07-28` revision (SEP-2575) replaced it with `subscriptions/listen`, and the SDK server refuses the old method on that revision: *"The method 'resources/subscribe' is not available on protocol version '2026-07-28'."* Found by an interop test failing, not by reading a changelog. This is PRD §4's spec-velocity argument arriving in the working tree, six days after the audit that made it. |
| 2026-08-03 | Sampling is reached only when elicitation is unavailable, never after a decline | `append_study_note` looks for a title in one order: the user, then the model, then the default. A user who declines to name their note ends the search — asking the model behind their back would not be a fallback, it would be ignoring them. A client that offers neither still gets its note saved. |
| 2026-08-03 | A model's suggested title is sanitised before it reaches the notes file | The title is written as `## {title}`. A model that answers with two lines, or wraps its answer in quotes, or adds commentary, would corrupt the markdown for every note after it. This is generated content going into a structured document, not a string being logged — first line only, stripped of quote and heading characters, cut to 80. A test drives a deliberately badly-behaved model. |
| 2026-08-03 | Sampling is deprecated on `2026-07-28` but, unlike subscribe and setLevel, not removed | Its interop tests run on the default revision and pass. So the three SEP-2577 features are in two different states, and the tests record which is which rather than assuming they move together. |
| 2026-08-03 | Elicitation went into an existing tool rather than a new one built to demonstrate it | `append_study_note` already had an optional `title` that silently defaulted to "Note". Asking the user is what the argument wanted all along, so the feature landed where it was actually useful instead of in a tool whose only purpose is the demo. Declining is treated as an answer: the note is still saved under the default, because losing a note over an unanswered question is the wrong trade. |
| 2026-08-03 | `AppendNoteAsync` extracted so the unit tests survive elicitation | The tool now takes an `McpServer` to ask its question, and a test cannot construct one — `McpServer` is abstract with an `[Experimental]` constructor, the same wall `F2-08` hit. The write itself moved to an internal method the tests drive directly, which is the split the file already used for `FormatEntry`. |
| 2026-08-03 | The progress test's wait was widened from 5 s to 20 s after one flake in a full-suite run | It passed alone and failed once with the whole suite running several server subprocesses at a time. The SDK dispatches notification handlers without awaiting them, so the reports can still be in flight when the call returns, and load widens that gap. The assertion was not weakened — only the patience. |
| 2026-08-03 | Structured output for three tools, not all five | `calculate_expression`, `get_current_datetime` and `scan_workspace` answer with data, so they publish an `outputSchema` and fill `structuredContent`. `read_text_file` and `append_study_note` answer with a document and a confirmation — wrapping prose in JSON only makes the client unescape it again, and the text block is what a person reads in Claude Desktop. The rule written down: tools whose answer is data get a schema, tools whose answer is words do not. |
| 2026-08-03 | Every tool now states its annotations explicitly, including the ones that look like defaults | The spec's defaults are the cautious ones — not read-only, destructive, non-idempotent, open-world — so a tool that says nothing looks as dangerous as the worst tool in the list, and a client auto-approving on those hints will prompt for everything. `read_text_file` saying `readOnly: true` is not noise; it is the difference between one prompt and none. |
| 2026-08-03 | **`F3-07` needed a long-running tool, so `scan_workspace` was added — a fifth tool outside the Phase 5 list** | Progress notifications cannot be demonstrated by four tools that each return in a millisecond. The tool walks the resource provider's document list, which already existed, and reports one step per document, so it is roughly thirty lines rather than a new capability. It does not pre-empt any Phase 5 task: `list_directory`, `write_text_file` and `search_files` are all still unwritten. |
| 2026-08-03 | The last progress report of a run is expected to be dropped, and the test says so | The final report is issued immediately before the tool returns, and the response overtakes it — the SDK stops routing notifications for a request once that request is answered. Reporting *before* each document instead would deliver the last notification but never report the run as complete, which is worse. The test asserts `n-1` reports rather than pretending the race is not there. |
| 2026-08-03 | Progress reports are asserted as a set, not a sequence | They arrive out of order at the client: `Progress<T>` re-dispatches each callback onto the thread pool. Ordering is not something the server can be held to through that, so the test asserts distinct, in-range steps with the right total. It also collects them through its own `IProgress<T>` — `Progress<T>` would have appended to a list from several threads at once. |
| 2026-08-03 | **The 2026-07-28 revision deprecates Roots, Sampling and Logging (SEP-2577), and `F3-06` and `F3-10` target two of them** | Surfaced as a build failure, not a warning: `MCP9005` under `TreatWarningsAsErrors`. Decision taken with the project owner — implement both, suppress narrowly, document. Deprecated is not removed, every shipping client negotiates a revision where both work, and the alternative loses real capability for a revision no client speaks yet. The suppressions are file-scoped in `ClientLogBridge` (the whole type exists for that one feature) and single-line elsewhere, each naming SEP-2577, so a future reader deletes the right things when the SDK removes them. |
| 2026-08-03 | Logging implemented rather than skipped, because the SDK advertises the capability whether or not the server honours it | `McpServerImpl.ConfigureLogging` sets `ServerCapabilities.Logging = new()` unconditionally. Skipping `F3-06` would not have produced a server without logging; it would have produced one that advertises logging and never sends a message. Same reasoning as `listChanged` in `F3-03`, arrived at from the opposite direction. |
| 2026-08-03 | `logging/setLevel` is also gone on `2026-07-28`, replaced by a per-request `_meta/io.modelcontextprotocol/logLevel` field | Second instance of the same pattern as `resources/subscribe`, found the same way. The bridge learns which session to write to from the `setLevel` request itself, so on the newer revision it is never attached and no messages flow. Chasing the per-request field would mean a request filter feeding a mechanism the SDK client cannot currently drive — the same speculative work declined for subscriptions. Pinned by `Setting_a_level_is_refused_on_the_revision_that_removed_the_method`. |
| 2026-08-03 | Only `DotNetMcpServer.*` log categories are mirrored to the client | Forwarding the SDK's own categories does not merely add noise: sending a notification writes a log line, which would be sent, which writes a log line. The filter is what makes the bridge terminate. stderr still receives everything. |
| 2026-08-03 | The tools take `ILoggerFactory` and name their category as a constant, rather than taking `ILogger<T>` | A static class cannot be a type argument, so `ILogger<WorkspaceTools>` does not compile, and borrowing an unrelated type's name would put that name on every `notifications/message` the client sees. The category is part of the protocol surface here, so it is written down. |
| 2026-08-03 | Per-resource subscriptions ship for `2025-11-25` and earlier; on `2026-07-28` only `list_changed` flows | Under SEP-2575 the SDK owns the subscription list and exposes no server-side hook — `_activeSubscriptions` is private and `SubscribeToResourcesHandler` is never invoked, confirmed in `McpServerImpl`. A server on that revision cannot learn which URIs a client follows through the public API. Reaching it would mean a message filter peeking at raw `subscriptions/listen` frames, for a path no SDK client can currently send — the client convenience API still emits the removed method. Building that would be writing to an interface nothing implements. Recorded instead, with `Subscribing_is_refused_on_the_revision_that_removed_the_method` pinning the boundary as an executable fact. Revisit when the SDK client gains a `subscriptions/listen` API. |
| 2026-08-03 | `notifications/resources/list_changed` implemented alongside `F3-03`, which asked only for subscriptions | The `FileSystemWatcher` the task requires already sees files appear and disappear. Advertising `resources` without `listChanged` while the server demonstrably knows the list changed would be a capability declared false out of laziness rather than design. The watcher starts on the first `resources/list` or `resources/subscribe`, so a session that only calls tools never watches the filesystem. |
| 2026-08-04 | **CI ran for six hours because the hand-written artifact deadlocked, and the job had no timeout** | `JsonRpcConnection.ReadMessageAsync` called `AdvanceTo(consumed, examined)` with `examined` at the end of the whole buffer after slicing one line off it, which tells the `PipeReader` to wait for new bytes. Two messages arriving in one read — what a loaded runner produces when a client writes before the server has read — left the second one unread forever. Every local run on Windows passed; the evidence was in CI, where the suite stopped partway through `HandwrittenServerInteropTests` and the job log ended with `Terminate orphan process: (Mcp.Protocol.Handwritten)`. The `2026-08-02` run hit GitHub's 6h ceiling; the `2026-08-04` run was cancelled at ten minutes. Two changes: the read is fixed and pinned by `Two_messages_arriving_in_one_read_are_both_delivered`, and the job now carries `timeout-minutes: 15` so the next hang costs fifteen minutes instead of six hours. The artifact is frozen against new spec revisions, not against being wrong. |
| 2026-08-04 | `ProgressInteropTests.A_long_running_tool_reports_progress_as_it_goes` is still flaky under a full-suite Release run | Failed once and passed on the immediate re-run, and passes alone. Same cause as the 2026-08-03 entry that widened its wait from 5 s to 20 s: the SDK dispatches notification handlers without awaiting them, and a suite running several server subprocesses at once widens the gap. Recorded rather than widened again — the next person to touch it should make the test wait on the report count instead of on a clock, not raise the number. |
| 2026-08-02 | A test asserting that a missing `openAI:model` fails validation was wrong and was rewritten | `Model` has a default of `gpt-4o-mini`, so `[Required]` is satisfied and no failure occurs. Unlike `ApiKey`, which has no default, a missing model is not a configuration error. The test now pins the fallback instead. Recorded because the failure came from the test's premise, not the code. |

---

## How to use this file

1. Set the task to 🟦 when you start it, ✅ when it is **verified** — not when the code compiles.
2. Commit with a conventional subject and name the task ids in the body (`Closes F1-07`). The `writing-commits` skill holds the format.
3. When a phase closes, tick its **Done when** boxes, update the progress bars and the metrics table, and move the **Current status** header to the next phase.
4. Any departure from the PRD goes in the decision log with its reason — including tasks you drop. A dropped task with a recorded rationale reads as judgement; a silently missing one reads as an unfinished project.
