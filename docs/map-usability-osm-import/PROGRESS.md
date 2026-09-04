# Progresso — usabilidade do mapa e importação OSM

- [x] Pesquisa técnica
- [x] Plano de implementação
- [x] Vértices e posição inicial
- [x] Foco pelos cards
- [x] Importação OSM
- [x] Testes
- [x] Publicação

## Decisões

- Conversão `.osm` local, sem persistir o arquivo bruto.
- Seleção explícita quando houver vários limites.
- Suporte a caminhos fechados e relações compostas.

## Validação final

- 9 testes do aplicativo e 2 testes do painel administrativo aprovados.
- 11 testes de domínio e 7 testes integrados da API aprovados.
- Compilação de produção dos dois front-ends e da API aprovada.
- Versão publicada nos dois subdomínios, com cache de entrada atualizado.

## Correção de renderização — 24/08/2026

- Corrigida a atualização das fontes GeoJSON durante o carregamento dos blocos raster do OpenStreetMap.
- O sistema não depende mais do evento inicial de carregamento para exibir pontos, polígonos ou destaques após zoom e movimentação.
- Mapa-base suavizado e contornos reforçados para melhorar o contraste.
- O clique em um card agora destaca a geometria selecionada em laranja, além de centralizá-la.
- Cenário visual de desenvolvimento criado com uma geometria real cadastrada.
- Validação visual confirmou a ordem: OSM, áreas cadastradas e, por último, desenho/destaque.
- 10 testes do aplicativo e 2 testes do painel administrativo aprovados após a correção.

## Correção do worker vetorial — 24/08/2026

- Identificada a causa da ausência persistente de polígonos no ambiente publicado: o worker do MapLibre não era incluído pelo Vite e a rota inexistente devolvia o HTML do aplicativo.
- Configurado o worker pelo pipeline oficial `?worker&url` do MapLibre 6 para Vite.
- A publicação agora entrega um worker JavaScript autônomo e versionado, usado por bairros, microrregiões, imóveis e pontos de desenho.
- Mantida uma rota de compatibilidade para sessões que ainda tenham o pacote antigo em cache.
- Adicionado teste de regressão para garantir que a URL do worker permaneça configurada.
- 11 testes do aplicativo, 2 testes do painel administrativo, lint e compilação de produção aprovados.
- Validação visual confirmou as camadas de preenchimento e contorno do bairro e o destaque de seleção acima do OpenStreetMap.
