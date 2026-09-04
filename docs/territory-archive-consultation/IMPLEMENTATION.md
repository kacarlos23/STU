# Implementação da consulta de microrregiões arquivadas

## Etapa 1 — API protegida

- Consultar somente microrregiões arquivadas da UBS autorizada.
- Retornar geometria, vínculos, cor, agente, data de arquivamento e token de concorrência.
- Manter arquivadas fora do endpoint do mapa operacional.

## Etapa 2 — Interface

- Adicionar o campo `Exibir` com opções `Ativas` e `Arquivadas`.
- Identificar visualmente os cards arquivados.
- Mostrar no mapa somente o contorno arquivado selecionado.
- Permitir edição e histórico enquanto o registro permanece arquivado.

## Etapa 3 — Validação

- Testar escopo por UBS.
- Confirmar que o mapa ativo não recebe registros arquivados.
- Confirmar que a edição preserva o arquivamento e acrescenta versão.
- Executar compilação, análise estática e testes completos antes da publicação.

## Etapa 4 — Reativação segura e reaproveitamento

- Substituir o índice global de código por índices exclusivos parciais de nome e código aplicados somente a registros ativos.
- Permitir que uma microrregião arquivada seja editada mesmo quando seus identificadores ou geometria já forem usados por uma área ativa.
- Validar nome, código, bairros ativos e interseção espacial antes da reativação.
- Adicionar a ação `Desarquivar` à consulta de arquivadas.
- Preservar versão e auditoria sem oferecer exclusão definitiva.
