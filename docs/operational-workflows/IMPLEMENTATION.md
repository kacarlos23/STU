# Implementação — fluxos operacionais

## Etapa 1 — Fila persistente

- Entidades de trabalho e notificação.
- Estados, tentativas, aprovação e resultado.
- Migração, índices e concorrência.

## Etapa 2 — API protegida

- Criar exportações e importações.
- Listar andamento, aprovar, repetir e baixar.
- Consultar e marcar notificações como lidas.
- Indicadores por UBS.

## Etapa 3 — Worker

- Reivindicar um trabalho por vez.
- Validar arquivos em área preparada.
- Importar propriedades atomicamente após aprovação.
- Exportar CSV, GeoJSON, KML e GeoPackage.
- Registrar conclusão, falha, auditoria e notificação.

## Etapa 4 — Interface

- Central de importações e exportações no app e no admin.
- Progresso, erros, aprovação e download.
- Central de notificações no cabeçalho.
- Indicadores de rascunhos e cobertura.

## Critério de conclusão

Trabalhos são retomáveis e auditados; importações inválidas não alteram dados oficiais; a aprovação insere todo o lote ou nada; downloads respeitam a UBS; notificações são individuais.

## Modelo familiar independente — 24/09/2026

Importações aceitam `familyNumber` e `familyResponsibleName` juntos ou ambos vazios. A prévia separa imóveis, famílias e vínculos. A aprovação cria o lote atomicamente; número já reservado exige vínculo explícito, inclusive se a família estiver arquivada. Exportações identificam separadamente imóvel, família atual e vínculo, com acesso por permissões de imóveis e famílias. Consulte [Famílias](../families/IMPLEMENTATION.md).
