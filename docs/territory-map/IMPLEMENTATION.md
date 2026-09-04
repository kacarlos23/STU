# Implementação — território e mapa

## Entrega

1. Modelo espacial de bairros, microrregiões e versões.
2. Políticas de `map.view` e `territory.manage`, incluindo permissão curinga do administrador global.
3. API GeoJSON com escopo de UBS, validação espacial, pré-visualização, CRUD, arquivamento e histórico.
4. Tela territorial no aplicativo principal com consulta, desenho e importação GeoJSON.
5. Tela territorial no painel global, com seleção da UBS.
6. Testes de domínio, integração de autorização/isolamento/versionamento e interface.
7. Migração, publicação e verificação nos dois subdomínios.

## Contratos principais

- `GET /api/territories/map?healthUnitId=&atUtc=`
- `GET /api/territories/reference-data?healthUnitId=`
- `POST /api/territories/neighborhoods`
- `PUT /api/territories/neighborhoods/{id}`
- `POST /api/territories/neighborhoods/{id}/archive|restore`
- `POST /api/territories/microregions/preview`
- `POST /api/territories/microregions`
- `PUT /api/territories/microregions/{id}`
- `POST /api/territories/microregions/{id}/archive|restore`
- `GET /api/territories/microregions/{id}/versions`

## Critério de conclusão

Usuários com `map.view` veem somente dados autorizados. Usuários com `territory.manage` alteram somente a própria UBS. O administrador global escolhe a UBS. Toda mudança é pré-validada, transacional, auditada, versionada e reconstruível por data.
