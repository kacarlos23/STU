# Configurações da UBS — plano de implementação

Status: proposta institucional para revisão, em 21/10/2026. O plano de UBS abaixo permanece futuro. A tela de Configurações da conta foi entregue separadamente em 22/10/2026, conforme o escopo a seguir.

## Configurações da conta — entrega de 22/10/2026

Disponível pelo menu lateral, pelo menu do usuário e por Mais no celular. Cada usuário edita apenas seu próprio nome, foto e preferência Claro/Escuro. A preferência fica gravada, mas a aplicação visual do tema será implementada posteriormente. Salvar é explícito; cancelar restaura os valores carregados e sair com alterações pede confirmação. Conflitos entre sessões preservam o preenchimento e exigem recarregar.

As fotos PNG/JPG/WebP são recortadas no navegador para PNG de 256 × 256 pixels. A API valida conteúdo, dimensões e tamanho; a imagem é privada à conta autenticada e não entra no cache do service worker. Login, UBS, funções e permissões não são editáveis nesse formulário.

O botão de senha envia um pedido interno aos gerentes ativos da própria UBS. Pedidos de gerentes e unidades sem gerente ativo seguem para a administração global. O pedido pendente não se duplica; o responsável recebe notificação e utiliza a redefinição existente na gestão de usuários. Ao atender, a pendência é encerrada, o solicitante recebe um aviso e o próximo acesso exige a troca da senha temporária. Nenhuma senha é enviada em notificações.

A migração adiciona somente os campos de perfil, preferência e pendência de senha, preservando usuários e vínculos. As gravações usam autenticação, CSRF, concorrência e auditoria existentes. Testes cobrem privacidade da foto, validação, concorrência, proteção de UBS/funções e o ciclo de pedido/atendimento.

## Alteração entregue nesta etapa

Pré-implantação e Liberação do piloto deixam a navegação desktop e o menu móvel do portal operacional. Os menus e os cartões correspondentes também saem da administração global. Não há mais entrada para essas telas nos dois dashboards.

Os serviços, registros de aprovação e auditoria existentes permanecem preservados. Esta mudança de visualização não altera o estado de aprovação de uma UBS. A limpeza mencionada para a entrega será uma operação separada; nenhum dado é reiniciado nesta etapa.

## Estrutura proposta

O gerente acessa **Configurações** pelo menu lateral ou por **Mais** no celular. A página mostra o nome da UBS e três abas: **Dados da UBS**, **Cobertura** e **Rótulos**. A gestão de visitas entra em uma segunda entrega.

```text
Configurações                         UBS selecionada
Dados da UBS | Cobertura | Rótulos

Prazo de cobertura
Prazo padrão da UBS: [ dias ]

Exceções por microrregião
Microrregião        Regra                 Prazo
MR01               Usar padrão           90 dias
MR02               Prazo específico       [60 dias]

                        [Cancelar] [Salvar alterações]
```

Os números acima ilustram o desenho; a tela deverá carregar os valores efetivamente gravados. Nenhum prazo será adotado automaticamente a partir do exemplo.

| Aba | Campos e informações | Botões e comportamento |
| --- | --- | --- |
| Dados da UBS | Nome, código e município cadastrados, apresentados para consulta. | **Abrir servidores** leva à tela existente da equipe, conforme a permissão do usuário. Alterações institucionais continuam em UBS e territórios no portal administrativo. |
| Cobertura | Prazo padrão da UBS, opção explícita **Sem prazo definido**, lista de microrregiões e escolha **Usar padrão** ou **Prazo específico** em cada uma. | **Salvar alterações** valida e grava o conjunto alterado em uma transação; **Cancelar** restaura os valores carregados. Alterar o padrão afeta apenas as regiões que o utilizam. |
| Rótulos | Busca, filtros Ativos/Arquivados/Todos, nome, cor, situação e quantidade de vínculos. | **Novo rótulo** abre formulário; **Editar** altera nome e cor; **Salvar** fecha o formulário e atualiza a lista; **Cancelar** descarta o formulário; **Arquivar** e **Reativar** pedem confirmação e preservam os vínculos existentes. |

Estados comuns: carregamento, lista vazia, falha com **Tentar novamente** e sucesso após confirmação da API. **Salvar alterações** fica desabilitado sem mudanças, durante o envio e sem conexão. Erros aparecem junto ao campo e preservam o preenchimento. Ao trocar de aba ou sair com alterações, reutilizar o diálogo existente para continuar editando ou descartar. A interface deve manter o padrão visual atual, inclusive fundos uniformes nos campos de busca.

## Primeira entrega: cobertura e rótulos existentes

### Cobertura

- Reutilizar `CoverageRule` e a definição atual de situação de cobertura. O prazo válido permanece um inteiro de 1 a 730 dias.
- Acrescentar persistência para o padrão da UBS e para a distinção entre herdar o padrão e manter uma exceção. A migração preserva as regras atuais como exceções. UBS e regiões sem regra continuam sem regra até uma gravação explícita.
- Ao remover uma exceção com **Usar padrão**, a região passa a herdar o prazo da UBS. Se não houver prazo padrão, apresentar **Sem prazo definido** e explicar o efeito antes de salvar.
- Mostrar um resumo antes de gravar: novo padrão e quantidade de regiões afetadas. A atualização do conjunto deve ser atômica e conferir versões, evitando gravação parcial.
- Após salvar, atualizar os indicadores de cobertura, listas, mapa e relatórios que consomem essas regras. A alteração reavalia a situação atual; datas e registros de visitas anteriores permanecem intactos.
- O cadastro de endereços continua sem geolocalização obrigatória. A configuração vale para os registros consultáveis e não cria pontos no mapa.

### Rótulos

- Extrair e reaproveitar a implementação de `Settings` em `PropertyWorkspace.tsx`, os componentes de interação e os endpoints de rótulos. Evitar uma segunda implementação independente.
- O backend já oferece criar, editar, arquivar e reativar; a tela atual em Cobertura oferece criar, arquivar e reativar. A nova área deve expor também a edição existente na API.
- Manter o limite atual de nome de 80 caracteres, validação de cor e unicidade por UBS, inclusive para rótulos arquivados. Exibir mensagem que permita localizar e reativar um nome já cadastrado.
- Arquivar impede novas seleções e mantém as associações e a consulta histórica. Renomear atua sobre o mesmo identificador e reflete o nome atual nas consultas; registrar nome e cor anteriores na auditoria.
- Manter rótulos no contexto de endereços/cobertura onde já são usados; não criar campos adicionais na família nem alterar o fluxo microrregião → agente → endereço → família.

Quando a nova tela estiver disponível, o botão **Configurar** dentro de Cobertura levará à mesma área, com a aba correspondente selecionada. O portal global deverá reutilizar esse componente com seleção explícita de UBS.

## Segunda entrega: parâmetros de visitas

Propor uma aba **Visitas** para habilitar ou desabilitar tipos e resultados já existentes. Os códigos de `VisitType` e `VisitOutcome` são enums fixos hoje; essa aba exige persistência de disponibilidade por UBS e validação no servidor.

Não permitir excluir códigos, mudar seu significado ou renumerá-los. Opções desabilitadas deixam novos lançamentos, mas continuam visíveis em registros anteriores, filtros, relatórios e exportações. Na edição de uma visita antiga, preservar o valor original se ele não for alterado. Validar as combinações mínimas permitidas antes de salvar. Alterações nas situações dos imóveis ficam fora desta entrega até haver uma regra de domínio específica.

## Acesso, persistência e integração

- Criar a permissão `health_unit.settings.manage`, atualmente inexistente no catálogo. Atribuí-la ao gerente padrão; o administrador global continua coberto por `*`. Migração de funções personalizadas deve preservar o acesso de quem já gerencia os parâmetros, sem remover `territory.manage` das demais ações territoriais.
- Aplicar a nova política nos endpoints de configuração e seus dados de referência, além da visibilidade da interface. Para consultar contagens e referências necessárias, fornecer uma consulta restrita à mesma permissão, sem conceder acesso ampliado a famílias ou servidores.
- Cada consulta e comando deve validar a UBS no servidor. Usuários locais não escolhem outra unidade; o administrador global seleciona a UBS explicitamente. Alterações exigem os mecanismos existentes de autenticação, CSRF e limitação de requisições.
- Reutilizar as tabelas de rótulos/regras, serviços e componentes atuais. Evoluir `/api/property-settings` para suporte ao padrão e gravação conjunta; não criar APIs paralelas para as mesmas entidades. Durante a transição, manter os contratos individuais compatíveis com a mesma política e concorrência.
- Acrescentar controle de concorrência aos parâmetros e rótulos, ausente nos contratos atuais. Se houver conflito, informar que os valores foram alterados por outro usuário e oferecer recarregar; nunca sobrescrever silenciosamente.
- Registrar autor, UBS, instante, valores anteriores e novos na auditoria. Respostas autenticadas continuam privadas e sem cache; nenhuma configuração da UBS será persistida pelo service worker.
- Funções e permissões, backups, monitoramento e auditoria continuam nas áreas administrativas existentes. Credenciais e parâmetros de infraestrutura continuam no servidor. O plano não inclui botão de limpeza de dados.

## Ordem de execução e aceite

1. Implementar permissão, escopo, controle de concorrência e migração do padrão de cobertura. Verificar preservação das regras e dos rótulos atuais em banco descartável com dados anteriores.
2. Extrair os componentes compartilhados; implementar as três abas e o redirecionamento de **Configurar** em Cobertura. Manter o comportamento de consultas e cadastros existentes.
3. Validar herança/exceções, remoção de exceção, limites, gravação atômica, conflitos, rótulos duplicados/arquivados, isolamento entre UBS e negativa de acesso por API. Conferir repercussão em indicadores e exportações, além de interação no celular, teclado e formulários não salvos.
4. Executar `scripts/verify.ps1`, revisar migrações, publicar os serviços afetados e confirmar ambos os portais e os endpoints de saúde em produção.
5. Implementar a aba Visitas como entrega posterior, com testes próprios de histórico e compatibilidade de importação/exportação.

## Base conferida no código em 21/10/2026

- `src/web/stu-app/src/features/dashboard/Dashboard.tsx`: o item desabilitado da proposta original agora abre as Configurações da conta, com o escopo descrito acima.
- `src/web/stu-admin/src/features/dashboard/AdminDashboard.tsx`: gestão global já separa UBS, usuários, funções, backups, monitoramento e auditoria.
- `src/web/stu-shared/src/properties/PropertyWorkspace.tsx`: componente `Settings`, rótulos e prazos individuais; o valor visual inicial de 90 dias não representa um padrão persistido da UBS.
- `src/STU.Api/Properties/PropertyEndpoints.cs`: `/api/property-settings/tags` e `/coverage/{microregionId}` usam `territory.manage`, validam escopo e registram auditoria; os contratos atuais não recebem versão de concorrência.
- `src/STU.Application/Security/StuPermissions.cs`: catálogo atual sem permissão própria de Configurações.
- `src/STU.Domain/Properties/PropertyEnums.cs`: tipos e resultados de visitas fixos.
