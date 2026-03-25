# Exemplos de Uso — DotNet MCP Server

Este documento mostra como o servidor MCP funciona na prática: o protocolo, as ferramentas e como o agente as encadeia.

---

## Estrutura dos exemplos

```
examples/
├── workspace/               ← arquivos que o servidor pode LER via read_text_file
│   ├── dotnet-concepts.md   ← anotações sobre C# e .NET
│   ├── csharp-snippets.md   ← snippets prontos para consulta
│   ├── tasks.md             ← lista de tarefas do projeto
│   └── budget-q1-2026.txt  ← dados de orçamento com expressões a calcular
│
└── jsonrpc/                 ← mensagens JSON-RPC brutas (request + response)
    ├── 01-initialize-request.json
    ├── 02-initialize-response.json
    ├── 03-initialized-notification.json
    ├── 04-list-tools-request.json
    ├── 05-list-tools-response.json
    ├── 06-call-get-datetime-request.json
    ├── 07-call-get-datetime-response.json
    ├── 08-call-calculate-request.json
    ├── 09-call-calculate-response.json
    ├── 10-call-read-file-request.json
    ├── 11-call-read-file-response.json
    ├── 12-call-append-note-request.json
    ├── 13-call-append-note-response.json
    ├── 14-error-path-traversal-request.json
    └── 15-error-path-traversal-response.json
```

---

## 1. Fluxo do protocolo MCP

O protocolo usa JSON-RPC 2.0 sobre stdin/stdout. Todo fluxo começa com um handshake:

```
Cliente                          Servidor
  │                                  │
  │──── initialize (id:1) ──────────▶│
  │◀─── result: serverInfo + caps ───│
  │                                  │
  │──── notifications/initialized ──▶│  ← sem id = notificação (sem resposta)
  │                                  │
  │──── tools/list (id:2) ──────────▶│
  │◀─── result: [ tool, tool, ... ] ─│
  │                                  │
  │──── tools/call (id:3) ──────────▶│
  │◀─── result: { content, isError }─│
```

**Diferença importante entre erro de protocolo e erro de ferramenta:**

| Tipo | Onde aparece | Exemplo |
|------|-------------|---------|
| Erro de protocolo | `response.error` | tool não existe, JSON inválido |
| Erro de ferramenta | `response.result.isError = true` | arquivo não encontrado, expressão inválida |

---

## 2. Ferramentas disponíveis

### `get_current_datetime`
Retorna a hora atual. Aceita um timezone IANA opcional.

**Request:**
```json
{
  "jsonrpc": "2.0", "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_current_datetime",
    "arguments": { "timezone": "America/Sao_Paulo" }
  }
}
```
**Response:**
```json
{ "result": { "content": [{ "type": "text", "text": "America/Sao_Paulo: Tuesday, 24 Mar 2026 09:34:56 -03:00" }], "isError": false } }
```

---

### `calculate_expression`
Avalia expressões matemáticas com `+`, `-`, `*`, `/` e parênteses.

**Request:**
```json
{
  "jsonrpc": "2.0", "id": 2,
  "method": "tools/call",
  "params": {
    "name": "calculate_expression",
    "arguments": { "expression": "(50 * 99) + (20 * 299) + (5 * 999)" }
  }
}
```
**Response:**
```json
{ "result": { "content": [{ "type": "text", "text": "Resultado: 15925" }], "isError": false } }
```

---

### `read_text_file`
Lê qualquer arquivo de texto dentro do workspace. Caminhos fora do workspace são bloqueados.

**Request:**
```json
{
  "jsonrpc": "2.0", "id": 3,
  "method": "tools/call",
  "params": {
    "name": "read_text_file",
    "arguments": {
      "path": "examples/workspace/dotnet-concepts.md",
      "maxCharacters": 3000
    }
  }
}
```

---

### `append_study_note`
Adiciona uma nota em `notes/study-notes.md` (criado automaticamente se não existir).

**Request:**
```json
{
  "jsonrpc": "2.0", "id": 4,
  "method": "tools/call",
  "params": {
    "name": "append_study_note",
    "arguments": {
      "title": "Records em C#",
      "note": "Records têm igualdade por valor e são imutáveis por padrão. Perfeitos para DTOs."
    }
  }
}
```

---

## 3. Cenário completo: sessão de estudos

Exemplo de como o agente encadeia múltiplas ferramentas numa única conversa:

**Usuário:** "Me resume os conceitos de async/await do arquivo de conceitos e salva uma nota resumida."

```
1. Agente chama: read_text_file("examples/workspace/dotnet-concepts.md")
   → lê o conteúdo, identifica a seção async/await

2. Agente processa o conteúdo com o LLM
   → gera um resumo

3. Agente chama: append_study_note(
     title="async/await — resumo",
     note="await libera a thread durante I/O. Nunca use .Result ou .Wait()."
   )
   → salva em notes/study-notes.md
```

---

## 4. Cenário: análise de orçamento

**Usuário:** "Quanto custa a infra por mês e qual a receita trimestral projetada?"

```
1. read_text_file("examples/workspace/budget-q1-2026.txt")
   → retorna o arquivo com as expressões

2. calculate_expression("850 + 420 + 130 + 75 + 60")
   → Resultado: 1535

3. calculate_expression("((50 * 99) + (20 * 299) + (5 * 999)) * 3")
   → Resultado: 47775

4. append_study_note(
     title="Orçamento Q1 2026",
     note="Infra mensal: R$ 1.535. Receita trimestral projetada: R$ 47.775."
   )
```

---

## 5. Como usar os arquivos do workspace com o servidor

Ao rodar o servidor, o `workspaceRoot` é configurado em `appsettings.json`:

```json
{
  "mcp": {
    "workspaceRoot": "."
  }
}
```

Com `workspaceRoot = "."` apontando para a raiz do repositório, você acessa os arquivos de exemplo assim:

```
read_text_file("examples/workspace/dotnet-concepts.md")
read_text_file("examples/workspace/tasks.md")
read_text_file("examples/workspace/budget-q1-2026.txt")
read_text_file("README.md")
```

---

## 6. Testes de cenário

Veja `tests/DotNetMcpServer.Tests/Examples/ScenarioTests.cs` para testes que demonstram esses fluxos completos com asserções verificáveis.

