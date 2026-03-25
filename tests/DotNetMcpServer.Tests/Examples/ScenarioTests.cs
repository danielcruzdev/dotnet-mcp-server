using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;

namespace DotNetMcpServer.Tests.Examples;

/// <summary>
/// Testes de cenário que demonstram fluxos realistas de uso do servidor MCP.
/// Cada teste simula o que um agente de IA faria ao receber uma pergunta do usuário.
/// </summary>
public sealed class ScenarioTests : IDisposable
{
    private readonly WorkspaceFixture _workspace = new();

    public void Dispose() => _workspace.Dispose();

    // -------------------------------------------------------------------------
    // Cenário 1: Leitura simples de documento
    // "Usuário pergunta sobre o conteúdo de um arquivo de conceitos"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_ReadConceptsFile_ReturnsFullContent()
    {
        // Arrange — agente recebe instrução: "leia o arquivo de conceitos .NET"
        var tool = new ReadTextFileTool(_workspace.Root);
        var args = new JsonObject { ["path"] = "docs/dotnet-concepts.md" };

        // Act
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Assert — conteúdo deve conter as seções criadas
        Assert.False(result.IsError);
        var text = result.Content[0].Text;
        Assert.Contains("async / await", text);
        Assert.Contains("Records", text);
        Assert.Contains("Nullable Reference Types", text);
        Assert.Contains("docs/dotnet-concepts.md", text); // cabeçalho do arquivo
    }

    [Fact]
    public async Task Scenario_ReadConceptsFile_WithSmallLimit_TruncatesAndSignals()
    {
        // Arrange — agente solicita leitura com limite menor que o conteúdo do arquivo
        var tool = new ReadTextFileTool(_workspace.Root);
        var args = new JsonObject
        {
            ["path"] = "docs/dotnet-concepts.md",
            ["maxCharacters"] = 200
        };

        // Act
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Assert — retorna truncado mas não erro
        Assert.False(result.IsError);
        Assert.Contains("[conteúdo truncado]", result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Cenário 2: Análise de orçamento (read + calculate)
    // "Usuário pede: quanto custa a infra por mês?"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_BudgetAnalysis_ReadThenCalculate()
    {
        // Passo 1 — agente lê o arquivo de orçamento
        var readTool = new ReadTextFileTool(_workspace.Root);
        var readResult = await readTool.ExecuteAsync(
            new JsonObject { ["path"] = "docs/budget.txt" },
            CancellationToken.None);

        Assert.False(readResult.IsError);
        Assert.Contains("850 + 420 + 130 + 75 + 60", readResult.Content[0].Text);

        // Passo 2 — agente identifica a expressão e calcula o total de infra
        var calcTool = new CalculateExpressionTool();
        var infraResult = await calcTool.ExecuteAsync(
            new JsonObject { ["expression"] = "850 + 420 + 130 + 75 + 60" },
            CancellationToken.None);

        Assert.False(infraResult.IsError);
        Assert.Contains("1535", infraResult.Content[0].Text);

        // Passo 3 — agente calcula a receita mensal projetada
        var receitaResult = await calcTool.ExecuteAsync(
            new JsonObject { ["expression"] = "(50 * 99) + (20 * 299) + (5 * 999)" },
            CancellationToken.None);

        Assert.False(receitaResult.IsError);
        Assert.Contains("15925", receitaResult.Content[0].Text);
    }

    [Fact]
    public async Task Scenario_BudgetAnalysis_QuarterlyRevenue()
    {
        // Agente calcula receita trimestral de uma vez só
        var calcTool = new CalculateExpressionTool();
        var result = await calcTool.ExecuteAsync(
            new JsonObject { ["expression"] = "((50 * 99) + (20 * 299) + (5 * 999)) * 3" },
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("47775", result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Cenário 3: Sessão de estudos (read + append note)
    // "Usuário pede: leia os conceitos e salve um resumo"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_StudySession_ReadConceptsThenSaveNote()
    {
        // Passo 1 — agente lê o arquivo de conceitos
        var readTool = new ReadTextFileTool(_workspace.Root);
        var readResult = await readTool.ExecuteAsync(
            new JsonObject { ["path"] = "docs/dotnet-concepts.md" },
            CancellationToken.None);

        Assert.False(readResult.IsError);

        // Passo 2 — agente (LLM) processaria o conteúdo e geraria um resumo.
        //           Aqui simulamos o resumo diretamente.
        var resumo = "await libera a thread durante I/O. " +
                     "Records têm igualdade por valor. " +
                     "Nullable Reference Types evitam NullReferenceException em tempo de compilação.";

        // Passo 3 — agente salva a nota resumida
        var noteTool = new AppendStudyNoteTool(_workspace.Root);
        var noteResult = await noteTool.ExecuteAsync(
            new JsonObject
            {
                ["title"] = "Conceitos .NET — Resumo da sessão",
                ["note"] = resumo
            },
            CancellationToken.None);

        Assert.False(noteResult.IsError);
        Assert.Contains("study-notes.md", noteResult.Content[0].Text);

        // Verifica que a nota foi persistida corretamente
        var notesPath = Path.Combine(_workspace.Root, "notes", "study-notes.md");
        Assert.True(File.Exists(notesPath));
        var savedContent = await File.ReadAllTextAsync(notesPath);
        Assert.Contains("Conceitos .NET — Resumo da sessão", savedContent);
        Assert.Contains("await libera a thread", savedContent);
    }

    [Fact]
    public async Task Scenario_StudySession_MultipleNotesAccumulate()
    {
        // Simula várias iterações de estudo na mesma sessão
        var noteTool = new AppendStudyNoteTool(_workspace.Root);

        string[] topicos =
        [
            "async/await — libera thread durante I/O",
            "Records — igualdade por valor, imutáveis",
            "Nullable — distinção entre string e string?"
        ];

        foreach (var (topico, i) in topicos.Select((t, i) => (t, i)))
        {
            var result = await noteTool.ExecuteAsync(
                new JsonObject
                {
                    ["title"] = $"Tópico {i + 1}",
                    ["note"] = topico
                },
                CancellationToken.None);

            Assert.False(result.IsError);
        }

        // Todas as notas devem estar no mesmo arquivo
        var notesPath = Path.Combine(_workspace.Root, "notes", "study-notes.md");
        var content = await File.ReadAllTextAsync(notesPath);

        Assert.Contains("Tópico 1", content);
        Assert.Contains("Tópico 2", content);
        Assert.Contains("Tópico 3", content);
        Assert.Contains("async/await", content);
        Assert.Contains("Records", content);
        Assert.Contains("Nullable", content);
    }

    // -------------------------------------------------------------------------
    // Cenário 4: Leitura de arquivo em sub-pasta
    // "Usuário pede: mostre o exemplo de código em src/"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_ReadFileInSubdirectory_ReturnsContent()
    {
        var tool = new ReadTextFileTool(_workspace.Root);
        var result = await tool.ExecuteAsync(
            new JsonObject { ["path"] = "src/exemplo.cs" },
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("IAsyncDisposable", result.Content[0].Text);
        Assert.Contains("DisposeAsync", result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Cenário 5: Segurança — path traversal bloqueado
    // O agente (ou prompt injection) tenta acessar arquivo fora do workspace
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_Security_PathTraversalIsBlocked()
    {
        var tool = new ReadTextFileTool(_workspace.Root);

        // Tentativas comuns de path traversal
        string[] maliciousPaths =
        [
            "../../etc/passwd",
            "../../../Windows/System32/drivers/etc/hosts",
            "docs/../../secret.txt",
        ];

        foreach (var path in maliciousPaths)
        {
            var result = await tool.ExecuteAsync(
                new JsonObject { ["path"] = path },
                CancellationToken.None);

            Assert.True(result.IsError,
                $"Deveria ter bloqueado o acesso ao path: {path}");
            Assert.Contains("Acesso negado", result.Content[0].Text);
        }
    }

    // -------------------------------------------------------------------------
    // Cenário 6: Arquivo grande — truncamento com sinal visual
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_LargeFile_TruncatesWithSignal()
    {
        var tool = new ReadTextFileTool(_workspace.Root);
        var result = await tool.ExecuteAsync(
            new JsonObject
            {
                ["path"] = "docs/large-file.txt",
                ["maxCharacters"] = 500
            },
            CancellationToken.None);

        Assert.False(result.IsError); // truncar não é erro
        var text = result.Content[0].Text;
        Assert.Contains("[conteúdo truncado]", text);
        // O final do arquivo não deve aparecer (foi truncado antes)
        Assert.DoesNotContain("Fim do arquivo grande.", text);
    }

    // -------------------------------------------------------------------------
    // Cenário 7: Data/hora em timezone brasileiro
    // "Usuário pergunta: que horas são em São Paulo agora?"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_Datetime_SaoPauloTimezone()
    {
        var tool = new GetCurrentDateTimeTool();
        var result = await tool.ExecuteAsync(
            new JsonObject { ["timezone"] = "E. South America Standard Time" },
            CancellationToken.None);

        // Não deve ser erro (timezone Windows válido para São Paulo)
        Assert.False(result.IsError);
        Assert.NotEmpty(result.Content[0].Text);
    }

    [Fact]
    public async Task Scenario_Datetime_InvalidTimezone_ReturnsToolError()
    {
        // Agente recebe timezone inválido — retorna isError=true, não exceção
        var tool = new GetCurrentDateTimeTool();
        var result = await tool.ExecuteAsync(
            new JsonObject { ["timezone"] = "Fuso/Invalido_XYZ" },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Fuso/Invalido_XYZ", result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Cenário 8: calculate_expression com casos reais do budget
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("850 + 420 + 130 + 75 + 60", "1535")]         // total infra mensal
    [InlineData("(2400 + 960 + 3600) / 12", "580")]            // custo mensal de licenças
    [InlineData("86 * 120", "10320")]                          // custo da sprint (horas * valor/h)
    [InlineData("(50 * 99) + (20 * 299) + (5 * 999)", "15925")] // receita mensal
    [InlineData("((50 * 99) + (20 * 299) + (5 * 999)) * 3", "47775")] // receita trimestral
    public async Task Scenario_BudgetExpressions_AllCalculateCorrectly(string expression, string expected)
    {
        var tool = new CalculateExpressionTool();
        var result = await tool.ExecuteAsync(
            new JsonObject { ["expression"] = expression },
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(expected, result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Cenário 9: Arquivo não encontrado — mensagem clara para o agente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Scenario_FileNotFound_ReturnsDescriptiveError()
    {
        var tool = new ReadTextFileTool(_workspace.Root);
        var result = await tool.ExecuteAsync(
            new JsonObject { ["path"] = "docs/nao-existe.md" },
            CancellationToken.None);

        Assert.True(result.IsError);
        // Mensagem deve incluir o nome do arquivo para o agente entender o que aconteceu
        Assert.Contains("nao-existe.md", result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Cenário 10: fluxo completo simulando uma tool registry com todas as tools
    // -------------------------------------------------------------------------

    [Fact]
    public void Scenario_ToolRegistry_AllToolsRegisteredAndDiscoverable()
    {
        // Simula o mesmo setup do Program.cs do servidor
        var registry = new ToolRegistry(new IMcpTool[]
        {
            new GetCurrentDateTimeTool(),
            new CalculateExpressionTool(),
            new ReadTextFileTool(_workspace.Root),
            new AppendStudyNoteTool(_workspace.Root)
        });

        var definitions = registry.ListDefinitions();

        // Todas as 4 ferramentas devem estar registradas
        Assert.Equal(4, definitions.Count);

        // Retornadas em ordem alfabética (como o agente as recebe no tools/list)
        var names = definitions.Select(d => d.Name).ToList();
        Assert.Equal("append_study_note", names[0]);
        Assert.Equal("calculate_expression", names[1]);
        Assert.Equal("get_current_datetime", names[2]);
        Assert.Equal("read_text_file", names[3]);

        // Todas têm schema de entrada definido
        Assert.All(definitions, definition =>
        {
            Assert.NotEmpty(definition.Name);
            Assert.NotEmpty(definition.Description);
            Assert.NotNull(definition.InputSchema);
        });
    }
}



