# Pesquisa — usabilidade do mapa e importação OSM

## Visão geral

Melhorar o cadastro territorial com vértices visíveis, foco pelo card, posição inicial em Teixeira de Freitas e leitura de exportações `.osm` do OpenStreetMap.

## Problema

Os pontos do desenho não permanecem evidentes, os cards não funcionam como navegação espacial, o mapa vazio inicia em São Paulo e o importador aceita apenas GeoJSON, enquanto a exportação básica do OpenStreetMap usa XML `.osm`.

## Abordagem recomendada

- Usar pontos GeoJSON individuais, numerados e com contorno contrastante durante o desenho.
- Manter os vértices após fechar o polígono, permitindo revisão e desfazer.
- Centralizar o mapa vazio em `[-39.7451701, -17.5384774]`, Teixeira de Freitas–BA.
- Fazer o card inteiro focar sua geometria com `fitBounds` ou `easeTo` para pontos.
- Interpretar `.osm` no navegador, sem enviar o arquivo bruto ao servidor.
- Detectar caminhos fechados e relações `multipolygon`/`boundary`; montar anéis compostos por vários caminhos e exigir escolha explícita quando houver mais de uma área.
- Salvar apenas a geometria escolhida em SRID 4326, usando a API territorial já protegida.

## Riscos e mitigação

- Um recorte OSM contém ruas e prédios: limitar candidatos a áreas fechadas nomeadas ou marcadas como área e mostrar seleção.
- Relações podem usar caminhos fragmentados: unir trechos pelos nós das extremidades em qualquer direção.
- Exportação pode estar incompleta: rejeitar relações sem anel externo fechado e explicar o problema.
- Arquivos excessivos: limitar a leitura local a 20 MB.

## Referências

- https://wiki.openstreetmap.org/wiki/Osm_format
- https://wiki.openstreetmap.org/wiki/Export
- https://wiki.openstreetmap.org/wiki/Relations/Multipolygon

