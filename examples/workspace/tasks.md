# Tarefas do Projeto MCP Server
## Em andamento
- [x] Implementar protocolo JSON-RPC base
- [x] Criar ferramenta read_text_file
- [x] Criar ferramenta calculate_expression
- [x] Criar ferramenta get_current_datetime
- [x] Criar ferramenta append_study_note
- [x] Escrever testes unitarios para todas as ferramentas
- [ ] Adicionar suporte a streaming de respostas
- [ ] Implementar ferramenta list_directory
## Backlog
- [ ] Adicionar autenticacao por token
- [ ] Criar ferramenta write_text_file
- [ ] Criar ferramenta search_in_files (grep simples)
- [ ] Adicionar cache de respostas para read_text_file
- [ ] Documentar o protocolo com exemplos OpenAPI-style
- [ ] Criar client CLI interativo
- [ ] Adicionar suporte a MCP Resources
## Concluido
- [x] Definir estrutura do projeto (Agent + Server + Shared)
- [x] Configurar xUnit e cobertura de testes
- [x] Implementar serializacao/desserializacao JSON-RPC
- [x] Integrar com OpenAI via HttpClient
- [x] Criar AgentSettingsLoader com suporte a variaveis de ambiente
- [x] Corrigir resolucao de workingDirectory para funcionar em qualquer IDE
## Notas
- Prioridade alta: streaming e list_directory
- Revisar seguranca do path traversal no ReadTextFileTool
- Considerar adicionar timeout configuravel nas chamadas de tool
