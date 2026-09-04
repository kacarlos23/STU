# Revisão do formulário de microrregião

Data: 31/08/2026

## Decisões

- O código da microrregião permanece como identificador curto e único dentro da UBS. Ele apoia busca, diferenciação, auditoria, histórico e notificações, enquanto o nome continua sendo a descrição legível da área.
- O formulário passa a explicar o código com o exemplo `MR-01`.
- A seleção manual de bairros foi removida da interface e do contrato de gravação.
- A API calcula os bairros atendidos a partir da geometria final da microrregião, depois do encaixe automático com áreas já existentes.
- Uma microrregião pode ser vinculada automaticamente a mais de um bairro quando atravessa seus polígonos.
- O limite continua precisando ficar contido na união dos bairros identificados quando há contornos poligonais disponíveis.
- Referências de bairro representadas apenas por ponto continuam compatíveis com o cálculo automático.
- O seletor nativo de cor foi substituído por uma paleta compacta do STU.
- A paleta fecha ao clicar novamente no controle, ao clicar fora ou após escolher uma cor.

## Validação

- Testes de integração confirmam o vínculo automático com um e com múltiplos bairros sem envio de identificadores pelo usuário.
- Testes de interface confirmam abertura, segundo clique, clique externo e seleção da cor.
- A verificação completa do repositório deve permanecer como requisito antes da publicação.
