# Melhorias do fluxo operacional — 10/09/2026

## Entregas do item 1

1. Imóveis: filtros separados para ativos, rascunhos, arquivados e todos.
2. Atalho «Cadastrar imóvel» da visão geral abre o formulário diretamente, respeitando permissões e a necessidade de microrregiões cadastradas.
3. Cobertura possui seção própria, com imóveis pendentes, fora do prazo, nunca visitados e em dia. Os indicadores consideram o conjunto autorizado filtrado, não apenas a página visível.
4. Busca, filtros, página e imóvel selecionado são preservados ao alternar seções durante a sessão. Preferências ficam somente na memória da interface e são descartadas ao sair da conta; respostas privadas não são persistidas.
5. Última visita destacada na ficha do imóvel e um único botão de registro de visita nesse fluxo.
6. Confirmação antes de descartar alterações nos formulários de imóveis, visitas, territórios e configurações de cobertura/etiquetas. Recarregar ou fechar a página utiliza o aviso nativo do navegador, sujeito às regras do próprio navegador.
7. Comparação dos limites territoriais salvos e propostos em dois mapas na mesma extensão e escala. A comparação não salva dados; a validação territorial existente continua obrigatória conforme o fluxo de edição.

Também foram padronizadas confirmações de arquivamento e mantidos os campos da visita quando o servidor rejeita o envio. Diálogos reutilizam o mecanismo acessível existente; Escape atua apenas no diálogo superior.

## Validação

- Execução integral de `scripts/verify.ps1`: concluída com sucesso em 10/09/2026.
- 23 testes unitários e 35 de integração do servidor aprovados.
- 37 testes do aplicativo e 8 do administrador aprovados: 103 testes no total.
- Análise estática, compilação de produção das duas interfaces e validação das configurações Docker aprovadas.
- Verificação visual no navegador integrado com dados fictícios: ficha do imóvel, aviso de descarte, tela de Cobertura e contornos da comparação lado a lado.
- Testes adicionais cobrem filtros, isolamento entre UBS, resumo antes da paginação, preservação de navegação, atalho de cadastro, erro de visita e comparação sem modificar a geometria original.

## Escopo e limites

- Nenhuma alteração de esquema do banco, exclusão de dados operacionais ou cadastro fictício em produção.
- Cenário visual local disponível somente em desenvolvimento (`workflow-visual-test.html`); não integra a entrada de produção.
- O compilador mantém um aviso sobre o tamanho do pacote cartográfico. Não impede a publicação; otimização de carregamento permanece para uma etapa de desempenho.
- Esta entrega não substitui homologação com usuários reais nem constitui nova auditoria completa de segurança ou teste de carga.

## Publicação e impedimento externo

- API, aplicativo e administrador recompilados e reiniciados com sucesso em 10/09/2026. Banco e seus volumes não foram reiniciados nem substituídos.
- Gateway local: HTTP 200 para ambos os hosts, entregando as novas entradas `index-C_dl5_8G.js` (app) e `index-Da4UP30G.js` (admin).
- Prontidão da API direta (8091) e pelo gateway (8088): HTTP 200. Consulta privada sem autenticação: HTTP 401. Banco: saudável, porta local 55432.
- Endereços públicos retornaram HTTP 530 com código Cloudflare 1033. O conector local `laudaapp-local` estava em execução, mas suas métricas indicavam zero conexões ativas.
- O túnel também atende outros endereços do Lauda. Nenhum reinício do túnel compartilhado ou alteração de DNS foi realizado; recuperação externa depende de confirmação do usuário para essa intervenção.

### Recuperação externa concluída

Após autorização explícita do usuário, o conector `laudaapp-local` foi reiniciado, sem alterar DNS ou o outro serviço Cloudflared da máquina. O túnel voltou a apresentar quatro conexões ativas. Os dois endereços públicos do STU responderam HTTP 200 e entregaram as novas entradas listadas acima; `/health/ready` respondeu HTTP 200 e a consulta privada sem autenticação continuou respondendo HTTP 401. O impedimento externo foi resolvido.
