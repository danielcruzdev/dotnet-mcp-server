using System.Text.Json.Nodes;
using DotNetMcpServer.Server.Tools;
using DotNetMcpServer.Shared.Mcp;

namespace DotNetMcpServer.Tests.Examples;

/// <summary>
/// Fixture que cria um workspace temporário com arquivos de exemplo realistas,
/// simulando o que um usuário teria em seu repositório.
/// </summary>
public sealed class WorkspaceFixture : IDisposable
{
    public string Root { get; }

    public WorkspaceFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), $"mcp-scenario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        CreateSampleFiles();
    }

    private void CreateSampleFiles()
    {
        // Conceitos .NET para leitura
        WriteFile("docs/dotnet-concepts.md", """
            # Conceitos .NET

            ## async / await
            O `await` libera a thread atual enquanto aguarda I/O.
            Nunca use `.Result` ou `.Wait()` — risco de deadlock.

            ## Records (C# 9+)
            Records têm igualdade estrutural por valor e são imutáveis por padrão.
            Ideais para DTOs e resultados de operações.

            ## Nullable Reference Types
            Ativados com `<Nullable>enable</Nullable>`. Distinguem `string` (nunca null)
            de `string?` (pode ser null), com verificação em tempo de compilação.
            """);

        // Dados de orçamento para usar com calculate_expression
        WriteFile("docs/budget.txt", """
            Custos mensais de infraestrutura (R$):
            Servidor : 850
            Banco     : 420
            CDN       : 130
            Logs      :  75
            CI/CD     :  60

            Total mensal : 850 + 420 + 130 + 75 + 60

            Projecao de receita mensal:
            Plano Basic      : 50 * 99
            Plano Pro        : 20 * 299
            Plano Enterprise :  5 * 999
            Total receita    : (50 * 99) + (20 * 299) + (5 * 999)
            """);

        // Lista de tarefas
        WriteFile("docs/tasks.md", """
            # Tarefas

            ## Concluído
            - [x] Implementar JSON-RPC
            - [x] Criar ferramentas base
            - [x] Escrever testes unitários

            ## Em andamento
            - [ ] Adicionar streaming
            - [ ] Criar ferramenta list_directory

            ## Backlog
            - [ ] Autenticação por token
            - [ ] Ferramenta write_text_file
            """);

        // Arquivo grande para testar truncamento
        WriteFile("docs/large-file.txt", new string('X', 6000) + "\nFim do arquivo grande.");

        // Sub-pasta com snippet
        WriteFile("src/exemplo.cs", """
            // Exemplo de uso do padrão IAsyncDisposable
            public sealed class Conexao : IAsyncDisposable
            {
                private readonly Stream _stream;

                public Conexao(Stream stream) => _stream = stream;

                public async ValueTask DisposeAsync()
                    => await _stream.DisposeAsync();
            }
            """);
    }

    public string FilePath(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public string ReadFile(string relative) =>
        File.ReadAllText(FilePath(relative));

    private void WriteFile(string relative, string content)
    {
        var full = FilePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.TrimStart('\r', '\n'));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

