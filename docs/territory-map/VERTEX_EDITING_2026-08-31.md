# Edição de vértices territoriais

Data: 31/08/2026

## Comportamento implementado

- Depois de fechar uma área, todos os vértices permanecem numerados e editáveis.
- Um vértice pode ser arrastado diretamente no mapa para reposicionar o limite.
- O primeiro e o último ponto do anel continuam sincronizados, preservando o fechamento válido do polígono.
- O botão **Adicionar ponto** ativa o modo de inserção; o próximo clique no mapa adiciona o vértice à borda mais próxima.
- O modo de inserção pode ser cancelado clicando novamente no mesmo botão.
- Depois de mover ou inserir um ponto, a pré-visualização de impacto é invalidada e precisa ser executada novamente antes de salvar uma microrregião.
- Polígonos e multipolígonos importados por GeoJSON ou OpenStreetMap também expõem seus vértices para edição.
- Os vértices e sua numeração aparecem somente enquanto o editor territorial está aberto; a visualização comum mantém apenas o contorno da área.
- A movimentação normal do mapa é pausada somente durante o arraste do ponto e restaurada ao terminar.

## Compatibilidade

- O ajuste automático contra limites já existentes continua sendo aplicado pela API após a edição.
- O cálculo automático dos bairros atendidos usa a geometria final editada.
- Nenhuma alteração de banco de dados foi necessária.

## Testes

- Fechamento do anel após mover o primeiro vértice.
- Inserção do novo ponto no segmento mais próximo.
- Exposição de todos os vértices de multipolígonos.
- Manutenção dos pontos numerados na camada de desenho.
