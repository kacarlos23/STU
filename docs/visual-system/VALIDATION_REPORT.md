# Relatório de validação do sistema visual

Data: 17 de setembro de 2026

## Resultado

As etapas A a E do guia foram implementadas e validadas. O portal operacional agora possui a identidade de cinco cores, painel com dados reais, prévia territorial autenticada, mapa com controles de camada e cobertura, navegação móvel e tratamento explícito de ausência de conexão.

As etapas F e G continuam deliberadamente pendentes. Elas exigem decisões operacionais e alterações de domínio: coordenada mantida da UBS, agendamento de visitas e catálogo de ações realizadas.

## O que foi conferido visualmente

### Painel amplo

- menu lateral e área de conta preservam hierarquia clara;
- quatro indicadores usam cores semânticas diferentes;
- mapa real e cobertura aparecem como o conteúdo principal;
- ações rápidas ficam numa coluna secundária;
- nenhuma contagem de pessoas é apresentada.

### Painel responsivo

- a barra lateral é substituída por navegação inferior;
- somente Visão geral, Mapa, Imóveis, Arquivos e Mais permanecem na barra;
- os itens de gestão são acessíveis por Mais;
- cartões passam de quatro para duas e depois uma coluna;
- a área segura inferior é respeitada.

### Mapa territorial

- microrregiões usam cor e contorno tracejado;
- rótulos aparecem sobre as áreas;
- bairros, microrregiões e imóveis podem ser ligados e desligados;
- geolocalização depende de ação e permissão do usuário;
- resumo de cobertura e lista textual permanecem disponíveis ao lado do mapa;
- percentuais por microrregião são derivados dos agregados reais.

## Validação automatizada

| Verificação | Resultado |
|---|---|
| Build do portal operacional | aprovado |
| Build do portal administrativo | aprovado |
| Lint dos dois portais | aprovado |
| Testes do portal operacional | 43 aprovados |
| Testes do portal administrativo | 8 aprovados |
| Testes unitários .NET | 23 aprovados |
| Testes de integração .NET | 46 aprovados |
| Build completo .NET | aprovado, sem avisos |

O teste de integração acrescentado confirma que indicadores, tendências, cobertura e resumos por microrregião não atravessam o escopo de outra UBS.

## Privacidade e offline

- a configuração da PWA continua sem cache de runtime para APIs e tiles privados;
- o navegador mostra aviso quando perde a conexão;
- gravações de imóvel, visita e território recusam envio offline com explicação;
- nenhum registro privado é salvo localmente para sincronização posterior.

## Observações técnicas

- A prévia do mapa é carregada tardiamente e o pacote inicial permaneceu separado do MapLibre. O build apresenta apenas o aviso esperado de que o chunk cartográfico ultrapassa 500 kB; ele só é baixado quando a prévia ou o mapa são necessários.
- A execução simultânea de todos os builds e testes excedeu a memória disponível da máquina. A mesma validação foi repetida sequencialmente e terminou integralmente aprovada.
- O mapa-base continua sendo o raster configurado do OpenStreetMap, suavizado visualmente. Um estilo cartográfico completamente próprio continua dependendo de tiles vetoriais ou de um arquivo local aprovado.
