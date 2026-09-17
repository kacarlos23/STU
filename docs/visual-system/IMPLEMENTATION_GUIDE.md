# Guia de implementação do sistema visual do STU

## 1. Objetivo

Aplicar ao Sistema Territorial das UBS uma identidade visual menos monocromática, baseada em cinco cores semânticas, e aproximar as experiências de painel, mapa e uso móvel dos esboços aprovados.

O trabalho deve manter as regras atuais de autorização, escopo por UBS, auditoria, acessibilidade e privacidade. O desenho nunca pode sugerir um dado que o STU não possua nem armazenar informações privadas no cache offline.

## 2. Resultado esperado

Ao término, o STU deverá oferecer:

1. identidade visual coerente em desktop e celular;
2. painel inicial com indicadores reais, comparação mensal, ações rápidas e prévia territorial real;
3. mapa com territórios identificáveis por cor e padrão, legenda, filtros de camada e resumo de cobertura;
4. navegação móvel inferior, avisos de conectividade e fluxos adequados para toque;
5. telas de imóveis e visitas visualmente integradas ao novo sistema;
6. testes automatizados e capturas em tamanhos representativos;
7. documentação suficiente para revisar, evoluir ou reverter cada etapa.

## 3. Decisões obrigatórias

### 3.1 Paleta de identificação

| Papel | Cor | Token | Uso principal |
|---|---:|---|---|
| UBS | `#176B5A` | `--color-ubs` | identidade, ações principais e estados positivos |
| Território | `#2F6BBD` | `--color-territory` | mapas, limites e navegação territorial |
| Visita | `#E9A23B` | `--color-visit` | visitas, atenção e itens próximos do prazo |
| Alerta | `#D95D52` | `--color-alert` | cobertura vencida, falhas e riscos |
| Gestão | `#7A5AAF` | `--color-management` | equipe, administração e implantação |

As superfícies usam tons neutros claros. Uma tela deve ter apenas uma cor de ação dominante; as demais identificam informação e não competem pela atenção.

### 3.2 Dados verdadeiros

- Não exibir quantidade de pessoas, pois o domínio não cadastra moradores.
- Usar imóveis ativos, identificações familiares, visitas e cobertura.
- Comparações percentuais precisam ser calculadas pelo servidor; não podem ser números decorativos.
- A posição de uma UBS só pode aparecer se houver coordenada explicitamente cadastrada.

### 3.3 Offline e privacidade

- Manter o aplicativo instalável como PWA.
- Permitir que o navegador reutilize apenas o shell e recursos públicos estáticos.
- Nunca colocar propriedades, visitas, usuários ou camadas privadas no cache da PWA.
- Quando estiver sem conexão, mostrar aviso claro e desabilitar operações que dependam da API.
- Não usar a frase “dados salvos no aparelho” enquanto não existir uma arquitetura específica e aprovada para dados privados offline.

### 3.4 Acessibilidade e interação

- Todo botão somente com ícone deve possuir nome acessível.
- Alvos de toque devem ter pelo menos 44 por 44 pixels.
- O foco deve permanecer visível e seguir para o título ao trocar de área.
- Cor não pode ser o único meio de diferenciar cobertura ou território; combinar cor, texto, ícone, traço ou padrão.
- Respeitar `prefers-reduced-motion` e não adicionar animações decorativas.
- Modais devem manter foco, fechar com Escape e devolver foco ao acionador.
- Barras fixas no celular devem respeitar `safe-area-inset`.

## 4. Arquitetura da implementação

### Etapa A — fundação visual

1. Criar tokens globais para cores, superfícies, texto, bordas, raios, sombras e escala de camadas.
2. Criar um componente de ícones SVG consistente e sem dependência de fonte externa.
3. Padronizar botões, cartões, mensagens, foco e estados desabilitados.
4. Remover glifos decorativos inconsistentes do painel.
5. Preservar os componentes e diálogos acessíveis já existentes.

Critérios de aceite:

- as cinco cores aparecem com papéis previsíveis;
- contraste de texto atende WCAG AA;
- nenhum ícone interativo depende apenas do seu desenho;
- nenhuma alteração afeta autenticação ou autorização.

### Etapa B — resumo e tendências do painel

1. Ampliar `GET /api/dashboard/summary` para retornar:
   - imóveis ativos;
   - identificações familiares ativas;
   - visitas do mês corrente;
   - visitas do mês anterior;
   - variação percentual de visitas;
   - alertas de cobertura;
   - microrregiões sem agente;
   - distribuição de cobertura;
   - resumo de cobertura por microrregião.
2. Manter o escopo da UBS e, para agente, somente suas microrregiões.
3. Exibir tendência somente quando houver base de comparação válida.
4. Exibir estado de carregamento estrutural e erro próximo ao painel.

Critérios de aceite:

- nenhum indicador vaza dados de outra UBS;
- percentuais são derivados das datas registradas;
- período anterior igual a zero não gera divisão inválida;
- testes cobrem gestor, agente e usuário global.

### Etapa C — painel visual e ações rápidas

1. Reorganizar cabeçalho, cartões e ações usando os tokens.
2. Trocar o mapa ilustrativo por uma prévia MapLibre somente leitura.
3. Carregar territórios e imóveis pelos endpoints autenticados já existentes.
4. Ajustar o enquadramento aos dados recebidos; usar Teixeira de Freitas como fallback.
5. Disponibilizar ações rápidas para:
   - cadastrar imóvel;
   - abrir mapa;
   - consultar cobertura;
   - registrar visita escolhendo primeiro um imóvel.
6. Exibir data local por `Intl.DateTimeFormat` e manter a unidade atual visível.

Critérios de aceite:

- a prévia não cria uma segunda fonte de dados;
- falha do mapa não impede uso do restante do painel;
- ações respeitam permissões;
- interface funciona sem mouse.

### Etapa D — mapa territorial aprimorado

1. Manter o MapLibre e o mapa-base raster atualmente configurado.
2. Acrescentar controles de visualização para territórios, imóveis e cobertura.
3. Adicionar rótulos de microrregião e legenda textual.
4. Usar contornos ou padrões diferentes para reforçar a identificação.
5. Criar painel de resumo com totais por microrregião a partir da resposta agregada do painel.
6. Adicionar geolocalização do navegador somente após ação do usuário.
7. Manter uma lista equivalente para quem não opera o mapa visualmente.

Critérios de aceite:

- o usuário consegue entender o estado sem depender apenas da cor;
- camadas podem ser ativadas e desativadas;
- o mapa continua funcional sem permissão de localização;
- propriedades permanecem restritas ao escopo autorizado.

### Etapa E — experiência móvel

1. Em larguras móveis, trocar a navegação lateral por uma barra inferior.
2. Mostrar apenas as quatro áreas operacionais principais na barra; gestão permanece em um menu acessível.
3. Respeitar a área segura do aparelho e reservar espaço para não cobrir conteúdo.
4. Transformar mapas e detalhes em sequência vertical ou painel inferior, conforme o contexto.
5. Tornar formulários de imóvel e visita de uma coluna.
6. Mostrar um aviso de conectividade controlado por `online` e `offline` do navegador.
7. Desabilitar submissões offline com explicação junto à ação.

Critérios de aceite:

- larguras de 320 a 430 pixels não produzem rolagem horizontal;
- controles essenciais têm no mínimo 44 pixels;
- conteúdo não fica escondido pela navegação inferior;
- conexão restaurada remove o aviso sem recarregar a página.

### Etapa F — unidade no mapa

1. Adicionar coordenada opcional à UBS como `Point` SRID 4326.
2. Criar migração e configuração espacial.
3. Expor coordenada apenas nos endpoints autorizados que precisem dela.
4. Permitir cadastro/edição pelo fluxo administrativo apropriado.
5. Mostrar marcador da UBS somente quando a coordenada existir.

Critérios de aceite:

- nenhuma posição é inferida pelo centro do território;
- coordenadas inválidas são rejeitadas;
- a ausência de coordenada mantém o mapa funcional.

Esta etapa depende de decisão operacional sobre quem manterá a localização das UBS. Até essa decisão, a interface deve omitir o marcador.

### Etapa G — evolução das visitas

1. Manter o registro atual de visita realizada como fluxo principal.
2. Se agendamento fizer parte da operação, criar entidade própria, sem misturar visita planejada com visita realizada.
3. Se ações executadas forem necessárias, criar catálogo controlado e relação auditável com a visita.
4. Não criar uma entidade `Família` apenas para reproduzir uma aba do esboço; manter “Identificação familiar” no imóvel.
5. Preservar o limite de 240 caracteres e a proibição de dados pessoais na observação.

Critérios de aceite:

- visita futura não aparece como visita já realizada;
- mudanças possuem auditoria e autorização;
- o formulário explica quais dados não devem ser escritos.

Esta etapa exige validação do processo de trabalho antes de alterar o domínio. A reforma visual utiliza apenas os campos de visita que já existem.

## 5. Ordem prática de alteração dos arquivos

1. `docs/visual-system/IMPLEMENTATION_GUIDE.md`: registrar e acompanhar este plano.
2. `src/web/stu-app/src/app/`: tokens, layout global, conectividade e navegação responsiva.
3. `src/web/stu-app/src/features/dashboard/`: painel, ícones e prévia real do mapa.
4. `src/STU.Api/Dashboard/`: agregados e tendências com escopo.
5. `tests/STU.IntegrationTests/Api/`: testes dos novos indicadores.
6. `src/web/stu-shared/src/territory/`: camadas, legenda e painel territorial.
7. `src/web/stu-shared/src/properties/`: ajustes móveis e estados offline.
8. testes React do aplicativo e componentes compartilhados.
9. capturas visuais e relatório final de validação em `docs/visual-system/`.

## 6. Validação

### Automatizada

- compilação TypeScript e build das aplicações;
- testes Vitest do aplicativo e do painel administrativo;
- testes .NET e integração;
- lint do frontend;
- verificação de que a PWA continua sem `runtimeCaching` privado.

### Visual

Capturar e conferir, no mínimo:

- desktop amplo: 1440 × 900;
- notebook: 1024 × 768;
- tablet: 768 × 1024;
- celular: 390 × 844;
- celular estreito: 320 × 700.

Em cada tamanho, verificar painel, mapa, lista de imóveis e formulário de visita.

### Acessibilidade

- navegação completa por teclado;
- foco visível;
- nomes acessíveis de botões;
- estrutura de títulos;
- contraste;
- zoom de 200%;
- redução de movimento;
- textos e padrões que não dependam exclusivamente de cor.

## 7. Estratégia de entrega e reversão

- Implementar em mudanças pequenas, mantendo o sistema compilável ao fim de cada etapa.
- Não apagar alterações locais anteriores.
- Mudanças de domínio devem possuir migração e teste antes de serem utilizadas pela interface.
- Caso a prévia do mapa falhe, o painel deve manter indicadores e um botão para abrir o mapa completo.
- Caso uma nova agregação falhe, não substituir o valor por dado fictício; mostrar estado indisponível.

## 8. Acompanhamento da execução

- [x] Guia criado e decisões registradas.
- [x] Etapa A — fundação visual.
- [x] Etapa B — resumo e tendências.
- [x] Etapa C — painel e prévia territorial.
- [x] Etapa D — mapa aprimorado.
- [x] Etapa E — experiência móvel e conectividade.
- [ ] Etapa F — coordenada opcional da UBS, pendente de definição operacional.
- [ ] Etapa G — agendamento e catálogo de ações, pendentes de definição operacional.
- [x] Testes automatizados.
- [x] Revisão visual em navegador nos modos amplo e responsivo.
- [x] Relatório final de implementação.

## 9. Limite desta execução

Serão implementadas agora as etapas A a E, que são suportadas pelo domínio e pelas regras atuais. As etapas F e G permanecerão documentadas como evoluções deliberadas porque alteram responsabilidades operacionais e o modelo de dados. O modo offline continuará seguro: shell instalável, aviso de conexão e nenhuma persistência privada no aparelho.
