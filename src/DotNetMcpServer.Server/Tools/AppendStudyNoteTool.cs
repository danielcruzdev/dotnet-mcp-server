using System.Text;
using System.Text.Json.Nodes;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Server.Tools;

public sealed class AppendStudyNoteTool : IMcpTool
{
    private readonly string _notesFilePath;

    public AppendStudyNoteTool(string workspaceRoot)
    {
        var notesDirectory = Path.Combine(Path.GetFullPath(workspaceRoot), "notes");
        Directory.CreateDirectory(notesDirectory);
        _notesFilePath = Path.Combine(notesDirectory, "study-notes.md");
    }

    public McpToolDefinition Definition => new()
    {
        Name = "append_study_note",
        Description = "Cria ou adiciona uma anotação em notes/study-notes.md.",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Título da anotação"
                },
                ["note"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Conteúdo da anotação"
                }
            },
            ["required"] = new JsonArray("note")
        }
    };

    public async Task<McpToolCallResult> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var note = arguments.GetString("note");
        if (string.IsNullOrWhiteSpace(note))
        {
            return McpToolCallResult.Fail("O campo 'note' é obrigatório.");
        }

        var title = arguments.GetString("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Anotação";
        }

        var now = DateTimeOffset.Now;
        var builder = new StringBuilder();
        builder.AppendLine($"## {title}");
        builder.AppendLine($"- Criado em: {now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine(note.Trim());
        builder.AppendLine();

        await File.AppendAllTextAsync(_notesFilePath, builder.ToString(), cancellationToken);

        return McpToolCallResult.Success($"Anotação salva em: {_notesFilePath}");
    }
}

