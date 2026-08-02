# Connecting the MCP server to a client

The server speaks MCP over stdio, so any compliant client can drive it: Claude Desktop, Claude
Code, VS Code, Cursor.

## The one rule

**Point the client at the compiled binary, never at `dotnet run`.**

`dotnet run` sends MSBuild output to stdout, and stdout is the protocol channel. A single build
warning corrupts the stream and the client reports a parse error or an empty tool list. This is
the same constraint the agent honours in `McpSettingsSetup.ResolveServerCommand`.

## Build first

```bash
git clone https://github.com/danielcruzdev/dotnet-mcp-server.git
cd dotnet-mcp-server
dotnet build DotNetMcpServer.slnx -c Release
```

The binary lands at:

| OS | Path (relative to the repository root) |
|---|---|
| Windows | `src\DotNetMcpServer.Server\bin\Release\net10.0\DotNetMcpServer.Server.exe` |
| Linux / macOS | `src/DotNetMcpServer.Server/bin/Release/net10.0/DotNetMcpServer.Server` |

Use an **absolute** path in every config below — clients do not resolve relative paths against
the repository.

## Claude Code

```bash
claude mcp add dotnet-mcp-server \
  --env MCP_WORKSPACE_ROOT=/absolute/path/to/a/workspace \
  -- /absolute/path/to/src/DotNetMcpServer.Server/bin/Release/net10.0/DotNetMcpServer.Server
```

Verify:

```bash
claude mcp list
```

## Claude Desktop

Edit the config file:

- **Windows** — `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS** — `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "C:\\path\\to\\src\\DotNetMcpServer.Server\\bin\\Release\\net10.0\\DotNetMcpServer.Server.exe",
      "args": ["--workspace-root", "C:\\path\\to\\a\\workspace"]
    }
  }
}
```

Restart Claude Desktop, then check the MCP indicator in the chat input. The four tools appear
under the server name.

## VS Code

Add to `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "dotnet-mcp-server": {
      "type": "stdio",
      "command": "/absolute/path/to/DotNetMcpServer.Server",
      "args": ["--workspace-root", "${workspaceFolder}"]
    }
  }
}
```

## Workspace root

The file tools refuse to read or write outside this directory. It is resolved in order:

1. `--workspace-root <path>` on the command line
2. the `MCP_WORKSPACE_ROOT` environment variable
3. the process's current directory

Point it at a directory you are willing to expose. Path containment is enforced in
`WorkspaceContext.ResolvePath`, though symlink escapes and case-sensitivity on Linux are known
gaps until Phase 5 (findings S1 and S2 in `.specs/PRD.md`).

## Tools

| Tool | What it does |
|---|---|
| `get_current_datetime` | Current date and time, optionally in an IANA/Windows timezone |
| `calculate_expression` | Arithmetic with `+`, `-`, `*`, `/` and parentheses |
| `read_text_file` | Reads a text file inside the workspace, truncated to a character limit |
| `append_study_note` | Appends a note to `notes/study-notes.md` inside the workspace |

## The hand-written server

`Mcp.Protocol.Handwritten` is a study artifact, not the product — see
[ADR-0001](adr/0001-official-sdk-with-handwritten-artifact.md). It is a working MCP server and
can be connected the same way, exposing `echo` and `add`:

```bash
claude mcp add handwritten-mcp \
  -- /absolute/path/to/src/Mcp.Protocol.Handwritten/bin/Release/net10.0/Mcp.Protocol.Handwritten
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Client shows no tools, or reports a parse error | Something wrote to stdout. Check the command is the binary, not `dotnet run` |
| `File not found` on startup | The path is relative, or the project was not built in that configuration |
| `Timezone '...' was not found` | `InvariantGlobalization` was enabled; it must stay `false` |
| Tools appear but every file read fails | The workspace root points somewhere else than you think — the server logs it to stderr on startup |

Piping JSON-RPC at the binary by hand is **not** a valid check: stdin closes immediately and
the response is written while the transport is already shutting down, so stdout looks empty on
a perfectly healthy server. Run `dotnet test` instead — the integration suite drives both
servers with the official SDK client.
