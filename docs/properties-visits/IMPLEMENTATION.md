# Implementação — imóveis e visitas

## Etapa 1 — Persistência e regras

- Entidades de imóvel, versão, visita, tag, vínculo e cobertura.
- Índices únicos e espaciais, validações e concorrência.
- Migração PostGIS e auditoria append-only.

## Etapa 2 — API escopada

- Consulta e mapa de imóveis com filtros.
- Cadastro, edição, arquivamento e restauração.
- Histórico de versões e reatribuição segura de identificadores.
- CRUD de visitas estruturadas.
- Configuração de tags e cobertura.
- Políticas `properties.view/manage` e `visits.view/manage`.

## Etapa 3 — Interface operacional

- Lista pesquisável e formulário geográfico de imóveis.
- Detalhe com identificadores, tags, situação de cobertura e histórico.
- Linha do tempo de visitas e formulário estruturado.
- Gestão de tags e prazo de cobertura.
- Acesso integral no painel global com seleção de UBS.

## Etapa 4 — Mapa progressivo

- Pontos e contornos no mapa territorial.
- Rótulos de casa e família a partir do zoom 16.
- Cores de cobertura e acesso ao cadastro do imóvel.

## Critério de conclusão

Reatribuições não criam duplicidades nem apagam histórico; agentes editam somente suas áreas; consultas respeitam a UBS; notas permanecem operacionais; imóveis aparecem com seus dois números no zoom de detalhe.

No cadastro manual, nenhuma coordenada é presumida: o operador precisa confirmar o ponto dentro da microrregião, pode ajustá-lo por arraste e recebe validação imediata antes da validação espacial definitiva da API.
