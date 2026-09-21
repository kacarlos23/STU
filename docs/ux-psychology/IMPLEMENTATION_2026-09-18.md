# Implementação de psicologia de UX no STU

Data: 18/09/2026
Escopo: ambiente local de teste do portal da UBS e componentes compartilhados com o painel administrativo.

## Objetivo

Transformar os princípios selecionados na revisão do vídeo em comportamentos verificáveis, sem criar urgência artificial, progresso fictício ou armazenamento local de dados privados.

## Etapa 1 — contexto antes da ação

- Indicadores da Visão geral mostram valor, denominador e percentual quando aplicável.
- Indicadores e linhas de cobertura abrem a listagem com o filtro correspondente já aplicado.
- A ação de visita informa quantos imóveis exigem atenção.
- Os cartões de Cobertura mostram percentuais em relação ao conjunto atual.
- Botões de exportação e importação descrevem o resultado esperado.

Validação: teste automatizado confirma que o indicador de alertas abre Cobertura com `coverage=pending`.

## Etapa 2 — progresso real e próxima ação

- Pré-implantação calcula o percentual apenas a partir dos controles retornados pela API.
- Liberação do piloto calcula o percentual apenas a partir das evidências retornadas pela API.
- A primeira pendência é apresentada como próxima ação.
- Atalhos só aparecem quando existe uma tela que o perfil atual realmente pode abrir.
- Pendências técnicas continuam explícitas, sem encaminhamento enganoso.

## Etapa 3 — valor e prévia antes da decisão

- A importação passou a exibir quatro estados: seleção, validação, revisão e efetivação.
- O nome e o tamanho do arquivo aparecem antes do envio.
- Um lote validado apresenta quantidade válida, erros e estágio do fluxo.
- A aprovação informa quantos imóveis serão gravados e reforça que nada foi efetivado antes dela.
- O CTA inclui a quantidade do lote.

## Etapa 4 — território e consequências

- O editor territorial mostra identificação, geometria, impacto e salvamento.
- O rascunho é preservado somente enquanto a edição está aberta; não é persistido no navegador.
- A prévia de impacto separa imóveis afetados e impedimentos e confirma que nada foi salvo.
- O salvamento continua bloqueado até a validação real da API.
- O arquivamento informa o que deixa a operação e o que permanece no histórico.

## Etapa 5 — padrões inteligentes e reversibilidade

- Novo imóvel começa ativo e ocupado, com explicação visível do padrão.
- Nova visita começa como rotina concluída e reutiliza a situação atual do imóvel.
- Filtros operacionais não sensíveis continuam preservados durante a sessão.
- Formulários com alterações continuam protegidos contra fechamento ou navegação acidental.
- Arquivamentos preservam histórico e usam confirmação com consequência operacional explícita.

## Evidências visuais

- `screenshots/overview-contextual.png`
- `screenshots/operations-preview.png`
- `screenshots/onboarding-progress.png`
- `screenshots/release-gate.png`
- `screenshots/territory-editor-steps.png`

## Verificações executadas

- Build TypeScript e Vite do portal e do painel administrativo.
- Testes do portal, incluindo filtros contextuais e impacto territorial.
- Revisão visual em 1440 px das cinco telas acima.
- Revisão responsiva no navegador local para Visão geral, Cobertura, importação, pré-implantação, liberação e editor territorial.

## Limites deliberados

- Progresso nunca é estimado: só muda com dados reais retornados pela API.
- O STU não grava rascunhos de imóveis, visitas ou território em `localStorage`.
- A prévia de importação usa os metadados validados pelo backend; uma grade linha a linha exigiria um endpoint paginado adicional e não foi simulada.
- Esta etapa não altera a paleta global; a refatoração roxa/lilás permanece um trabalho visual separado para evitar misturar comportamento e identidade cromática.
