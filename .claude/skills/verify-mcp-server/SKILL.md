---
name: verify-mcp-server
description: Use when checking whether the MCP server actually works — after changing a tool, the transport, the host wiring, or the launch command, or when a tool call returns nothing, the client shows no tools, the handshake appears to hang, or piped JSON-RPC produces empty output.
---

# Verifying the MCP server

## Core principle

**Only a real client proves the server works.** Everything else produces false results in both
directions — a shell pipe reports failure on a working server, and a passing unit test says
nothing about whether the tool is reachable over the protocol.

## Do this

```bash
dotnet build DotNetMcpServer.slnx --nologo     # must be 0 warnings, 0 errors
dotnet test  DotNetMcpServer.slnx --nologo -v q
```

The suite in `tests/DotNetMcpServer.Tests/Integration/SdkServerInteropTests.cs` launches the
built server as a **real subprocess** and drives it with the **official SDK client**. That is
the check that counts. `ServerLocator.ExecutablePath("DotNetMcpServer.Server")` resolves the
binary for the current configuration, so a stale build shows up as a clear file-not-found
rather than a mysterious hang.

To add a case, follow the existing fixture: it creates a temp workspace, connects one
`McpClient`, and cleans up in `DisposeAsync`.

## Traps that produce false results

### Piping NDJSON into the server prints nothing — and the server is fine

```bash
printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize",...}' | ./DotNetMcpServer.Server.exe
# stdout: empty. Server is NOT broken.
```

The pipe closes stdin immediately. Watch stderr and you will see
`transport completed reading messages` land *before* the request handlers run — the transport
begins shutting down while responses are still being written. **Never conclude the server is
broken from a shell pipe.** Use the interop tests.

### `IsError` is `bool?`, and success is `null`

```csharp
Assert.False(result.IsError);              // FAILS on success: actual is null, not false
Assert.NotEqual(true, result.IsError);     // correct
Assert.True(result.IsError);               // correct for the failure case
```

### stdout must carry nothing but protocol

If a client sees a parse error or refuses to connect, something wrote to stdout. Check:

- `Console.WriteLine` anywhere in `DotNetMcpServer.Server` — must be `ILogger`
- Logging not pinned to stderr — `Program.cs` sets `LogToStandardErrorThreshold`
- The server launched via `dotnet run` — MSBuild writes to stdout and corrupts the stream.
  The agent resolves the compiled binary in `AgentSettingsLoader.ResolveServerCommand`; keep
  it that way.

### Timezone tools fail with "not found on this system"

`InvariantGlobalization` is switched on somewhere. It disables ICU, so IANA ids like
`America/Sao_Paulo` stop resolving. It is pinned to `false` in `Directory.Build.props` with a
comment — do not flip it.

## Verifying against a real desktop client

```bash
claude mcp add dotnet-mcp-server -- <repo>/src/DotNetMcpServer.Server/bin/Debug/net10.0/DotNetMcpServer.Server.exe
```

Point the command at the **compiled binary**, never at `dotnet run`. See `docs/INSTALL.md`
once it exists.

## Quick reference

| Symptom | Cause |
|---|---|
| Empty stdout when piping JSON | Test artifact — stdin EOF races the response write. Use interop tests |
| `Assert.False(IsError)` fails, actual `null` | `IsError` is `bool?`; success is `null` |
| Client reports invalid JSON / won't connect | Something wrote to stdout — find it |
| `Timezone '...' was not found` | `InvariantGlobalization` is on |
| `FileNotFoundException` from `ServerLocator` | Build the solution first |
