# Pesquisa — território e mapa

## Decisões

- O banco continua sendo a fonte de verdade geográfica, com PostGIS e SRID 4326.
- Bairros aceitam `Point`, `Polygon` ou `MultiPolygon`; o ponto é a alternativa quando não existe contorno confiável.
- Microrregiões exigem `Polygon`/`MultiPolygon`, pertencem a um bairro e a uma UBS, e podem ter um agente ativo da mesma UBS.
- Limites inválidos são recusados. Microrregiões podem encostar, mas não podem ter interiores sobrepostos no mesmo bairro.
- Cada alteração gera auditoria e um snapshot territorial imutável. A consulta histórica usa o último snapshot existente na data solicitada.
- A API entrega GeoJSON já filtrado pela UBS do usuário. O administrador global pode selecionar qualquer UBS.
- O cliente usa uma fonte GeoJSON do MapLibre e camadas progressivas. Limites e pontos são carregados juntos e estilizados por tipo.
- O mapa-base é configurável. O padrão inicial usa os tiles raster públicos do OSM apenas para visualização interativa e mantém atribuição visível; para a expansão, deve ser substituído por provedor contratado ou tiles próprios.

## Referências primárias

- MapLibre GL JS: fontes GeoJSON podem receber objetos inline e ser atualizadas sem recriar o mapa: https://maplibre.org/maplibre-gl-js/docs/API/classes/GeoJSONSource/
- Npgsql: o plugin NetTopologySuite traduz operações espaciais do EF Core para PostGIS: https://www.npgsql.org/efcore/mapping/nts.html
- PostGIS `ST_IsValid`: valida geometrias segundo as regras OGC: https://postgis.net/docs/ST_IsValid.html
- PostGIS `ST_Intersects` e `ST_Touches`: permitem rejeitar interseção de interiores sem bloquear fronteiras compartilhadas: https://postgis.net/docs/en/ST_Intersects.html e https://postgis.net/docs/manual-3.7/en/ST_Touches.html
- OSMF Tile Usage Policy: exige atribuição, cache normal do navegador, ausência de download em massa e recomenda provedor alternativo ou hospedagem própria para maior carga: https://operations.osmfoundation.org/policies/tiles/
- NetTopologySuite GeoJSON4STJ: conversor oficial para `System.Text.Json`: https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON

## Riscos tratados

- Vazamento entre UBS: escopo aplicado em todas as consultas e mutações no servidor.
- Polígonos corrompidos: tipo, SRID, faixa de coordenadas e validade são validados antes da transação.
- Edição concorrente: cada entidade possui token de versão exigido nas alterações.
- Perda histórica: snapshots são somente de acréscimo e guardam geometria, vínculo, responsável e estado arquivado.
- Dependência de tiles comunitários: URL configurável e nenhuma funcionalidade de pré-busca/offline no MVP.
