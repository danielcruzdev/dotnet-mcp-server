using System.Text;
using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server.Tools;

public sealed class ReadTextFileTool : IMcpTool
{
    private readonly string _workspaceRoot;

    public ReadTextFileTool(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public McpToolDefinition Definition => new()
    {
        Name = "read_text_file",
        Description = "Lê um arquivo de texto dentro do workspace do projeto.",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Caminho relativo ao workspace. Ex.: README.md"
                },
                ["maxCharacters"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Limite máximo de caracteres retornados (200-8000).",
                    ["minimum"] = 200,
                    ["maximum"] = 8000
                }
            },
            ["required"] = new JsonArray("path")
        }
    };

    public async Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var relativePath = arguments.GetString("path");
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return McpToolCallResult.Fail("O campo 'path' é obrigatório.");
        }

        var maxCharacters = arguments.GetInt("maxCharacters", 1600, 200, 8000);

        string absolutePath;
        try
        {
            absolutePath = ResolvePathInsideWorkspace(relativePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            return McpToolCallResult.Fail(exception.Message);
        }

        if (!File.Exists(absolutePath))
        {
            return McpToolCallResult.Fail($"Arquivo não encontrado: {relativePath}");
        }

        var content = await File.ReadAllTextAsync(absolutePath, cancellationToken);
        var truncated = content.Length > maxCharacters;

        var finalContent = truncated ? content[..maxCharacters] : content;
        if (truncated)
        {
            finalContent += "\n\n[conteúdo truncado]";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Arquivo: {relativePath}");
        sb.AppendLine($"Caracteres retornados: {finalContent.Length}");
        sb.AppendLine();
        sb.Append(finalContent);

        return McpToolCallResult.Success(sb.ToString());
    }

    private string ResolvePathInsideWorkspace(string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));

        if (combined.Equals(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return combined;
        }

        var rootWithSeparator = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _workspaceRoot
            : _workspaceRoot + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Acesso negado: o arquivo precisa estar dentro do workspace.");
        }

        return combined;
    }
}

