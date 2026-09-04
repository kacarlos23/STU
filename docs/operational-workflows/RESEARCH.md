# Pesquisa — fluxos operacionais

## Visão geral

A Fase 5 transforma importações e exportações em trabalhos rastreáveis, inclui notificações internas e consolida indicadores operacionais sem ampliar o escopo para dados de moradores.

## Abordagem recomendada

- Persistir trabalhos em uma fila no PostgreSQL, processada pelo `STU.Worker`.
- Armazenar arquivos em diretório dedicado e compartilhado somente entre API e worker, sempre com nomes aleatórios.
- Validar importações integralmente antes da aprovação e inserir todos os imóveis em uma única transação.
- Disponibilizar CSV, GeoJSON, KML e GeoPackage; o GeoPackage será produzido pelo GDAL no worker.
- Entregar notificações no próprio sistema, com leitura individual e escopo por usuário.
- Manter todas as solicitações, aprovações, conclusões e falhas na auditoria.

## Segurança

- Limite de 20 MB e até 10.000 registros por arquivo.
- Extensões permitidas: `.csv`, `.json` e `.geojson`.
- Nome de armazenamento gerado pelo servidor, fora da árvore da aplicação.
- Downloads exigem autenticação, permissão e escopo da UBS.
- Arquivos nunca ficam disponíveis diretamente pelo Nginx.

## Fluxo de importação

1. Gerente ou administrador envia CSV/GeoJSON.
2. Worker valida colunas, geometria, microrregião e duplicidades.
3. Trabalho fica `Aguardando aprovação` com resumo, sem alterar dados oficiais.
4. Usuário aprova.
5. Worker revalida e grava tudo em uma única transação.

## Referências

- Tarefas em segundo plano e escopos: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
- Segurança em uploads: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0
- Conversão com GDAL/ogr2ogr: https://gdal.org/programs/ogr2ogr.html
