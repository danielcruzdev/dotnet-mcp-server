---
name: add-mcp-tool
description: Use when adding, renaming, or changing an MCP tool in DotNetMcpServer.Server — including the Phase 5 tools (list_directory, write_text_file, search_files, git_log, git_diff, http_fetch) and any tool that reads or writes workspace files.
---

# Adding an MCP tool

## Core principle

**The protocol surface and the logic are separate.** The `[McpServerTool]` method is a thin
adapter; the behaviour lives in an `internal` method or an injected service. That split is why
the logic is unit-testable without spawning a process, and why the interop tests stay small.

## The shape

Every tool is three things, in this order:

1. **Logic** — an `internal static` method (pure where possible) or a method on an injected
   service. Takes plain values, returns a `string`, throws `McpException` on failure.
2. **Adapter** — a `public static` method on a `[McpServerToolType]` class, decorated with
   `[McpServerTool(Name = "snake_case_name")]` and `[Description(...)]`. It does argument
   plumbing and nothing else.
3. **Tests** — unit tests against the logic in `tests/DotNetMcpServer.Tests/Tools/`, plus a case
   in `Integration/SdkServerInteropTests.cs` if the tool crosses a real boundary (filesystem,
   network, process).

```csharp
[McpServerToolType]
public static class WorkspaceTools
{
    [McpServerTool(Name = "read_text_file")]
    [Description("Reads a text file from inside the project workspace.")]
    public static async Task<string> ReadTextFile(
        WorkspaceContext workspace,                                   // injected from DI
        [Description("Path relative to the workspace root.")] string path,
        [Description("Maximum characters to return (200-8000).")] int? maxCharacters = null,
        CancellationToken cancellationToken = default)
    {
        // plumbing only — behaviour belongs in the logic method or the service
    }
}
```

## Rules

| Rule | Why |
|---|---|
| Tool names are `snake_case`, set explicitly via `Name =` | The method name would otherwise leak into the public contract; `examples/jsonrpc/` pins these names |
| Every parameter carries `[Description]` | It becomes the JSON schema the model reads to decide how to call the tool |
| Failures throw `McpException`, never return an error string | The SDK maps the exception to `isError`; a returned string reads as success |
| Services arrive as method parameters, resolved from DI | Registered in `Program.cs`; the SDK injects anything that is not a schema parameter |
| Never build a path by hand — go through `WorkspaceContext.ResolvePath` | One containment guard to audit instead of one per tool |
| Never write to `Console.Out` | stdout is the protocol channel. Use `ILogger`, which is pinned to stderr |

## Registration

Nothing to register. `Program.cs` calls `WithToolsFromAssembly()`, so a new
`[McpServerToolType]` is discovered automatically. A new *service* does need
`builder.Services.AddSingleton(...)`.

## Verifying

Unit tests alone do not prove the tool is reachable over the protocol. **REQUIRED:** use the
`verify-mcp-server` skill — it covers the interop harness and the assertion traps that make a
broken tool look like it passed.

## Common mistakes

- **Returning `McpToolCallResult`** — that type belongs to the frozen artifact in
  `src/Mcp.Protocol.Handwritten/`. Shipped tools return `string` and throw on failure.
- **Implementing `IMcpTool`** — same thing. That interface is artifact-only.
- **Putting logic in the adapter** — it becomes reachable only by spawning a process, so the
  fast unit tests can no longer cover it.
- **Forgetting `CancellationToken`** — long-running tools must accept and forward it.
