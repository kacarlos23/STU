# Progresso — fluxos operacionais

- [x] Pesquisa e decisões
- [x] Correção do desenho territorial
- [x] Modelo e migração
- [x] API de trabalhos e notificações
- [x] Worker de importação e exportação
- [x] Interface operacional
- [x] Testes e segurança
- [x] Publicação e verificação

## Decisões

- Fila persistente no PostgreSQL.
- Arquivos privados em volume dedicado.
- Importação em duas etapas, com aprovação explícita.
- Commit único por lote aprovado.
- PWA atualiza automaticamente o shell público e nunca armazena respostas de `/api`.
- `index.html`, `sw.js` e o registrador da PWA são servidos sem cache persistente.
