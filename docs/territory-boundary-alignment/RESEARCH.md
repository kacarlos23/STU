# Pesquisa — vínculos e encaixe de limites territoriais

## Visão geral

Esta evolução remove a exclusividade entre microrregião e bairro, ajusta automaticamente uma nova geometria às áreas já existentes do mesmo nível e permite cores configuráveis para bairros e microrregiões.

## Problema

- Uma microrregião pode atravessar ou atender mais de um bairro, mas o modelo atual exige exatamente um bairro.
- Desenhar limites vizinhos sem sobreposição ou lacuna exige precisão manual excessiva.
- Todas as áreas usam cores fixas, dificultando a identificação rápida no mapa.

## Casos de uso

- O gerente seleciona dois ou mais bairros para uma microrregião cujo limite atravessa ambos.
- Ao desenhar uma nova área ligeiramente sobre uma área existente, o STU preserva a existente e recorta somente a que está sendo salva.
- O mesmo comportamento vale para desenho manual e importações OSM/GeoJSON.
- O gerente escolhe uma cor por área e vê essa cor no mapa, nos cards e no histórico territorial.

## Abordagem recomendada

### Vínculo territorial

Substituir `Microregion.NeighborhoodId` por uma associação muitos-para-muitos explícita `MicroregionNeighborhood`. O contrato passa a receber `NeighborhoodIds`. O código da microrregião será único dentro da UBS, pois não existe mais um único bairro para compor essa identidade.

Snapshots de microrregião armazenarão os IDs de bairros em um vetor PostgreSQL `uuid[]`. Isso preserva a reconstrução histórica sem permitir que mudanças posteriores na tabela associativa alterem versões antigas.

### Encaixe automático

A geometria que está sendo criada ou editada é sempre a geometria de menor prioridade naquela operação. O servidor calcula:

`área ajustada = área enviada - união das áreas ativas já existentes do mesmo nível`

Assim, nenhuma área anterior é modificada implicitamente. Bairros são ajustados contra outros bairros poligonais; microrregiões são ajustadas contra outras microrregiões ativas. Um simples contato de fronteira não remove área. Se o resultado ficar vazio, o cadastro é recusado. Se o recorte produzir partes desconectadas, elas são preservadas como `MultiPolygon`.

A pré-visualização de microrregião devolverá a geometria já ajustada para conferência antes do salvamento. O salvamento recalcula o ajuste para evitar conflito concorrente. Bairros também são ajustados no servidor ao salvar.

### Cobertura dos bairros

Quando todos os bairros selecionados possuem polígonos, a união deles deve cobrir a microrregião ajustada. Se algum bairro tiver apenas um ponto de referência, a contenção completa não pode ser provada e o vínculo é aceito, preservando a regra anterior para bairros sem contorno disponível.

### Cores

Adicionar `Color` a bairro, microrregião e seus snapshots, validado como hexadecimal `#rrggbb`. A API inclui a cor no GeoJSON e o MapLibre usa expressão orientada pelos dados. O formulário terá seletor visual de cor e os cards mostrarão uma amostra.

## Riscos e mitigação

- **Recorte total:** recusar com mensagem clara.
- **Resultado multipartes:** manter todas as partes como `MultiPolygon`.
- **Condição de corrida:** recalcular no salvamento e manter o token de concorrência.
- **Alteração silenciosa:** informar no retorno e mostrar o recorte durante a pré-visualização da microrregião.
- **Histórico inconsistente:** persistir cor e IDs dos bairros em cada snapshot.
- **Vazamento entre UBS:** manter escopo de leitura e gestão no servidor; o ajuste pode considerar limites existentes sem revelar seus dados.

## Referências primárias

- PostGIS `ST_Difference`: https://postgis.net/docs/ST_Difference.html
- PostGIS `ST_UnaryUnion`: https://postgis.net/docs/ST_UnaryUnion.html
- Npgsql com NetTopologySuite: https://www.npgsql.org/efcore/mapping/nts.html
- Expressões de estilo do MapLibre: https://maplibre.org/maplibre-style-spec/expressions/
