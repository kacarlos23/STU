# Cadastro territorial por microrregião

O portal operacional abre em **Microrregiões**. O cadastro e a consulta seguem a sequência microrregião → agente de saúde → endereço → família.

- A microrregião recebe código, nome e um bairro ativo já cadastrado. O cadastro não exige desenho de limite.
- Uma microrregião pode ter vários agentes. O gestor seleciona um servidor ativo da UBS ou, com permissão para gerenciar servidores, cria uma conta de agente pelo serviço de identidades existente. A senha temporária aparece uma vez após salvar.
- **Salvar** no vínculo de agente fecha o formulário e mantém a lista da equipe aberta.
- Cada endereço pertence a um vínculo de agente com a microrregião. Logradouro, número ou S/N, bairro, complemento, CEP e referência são registros de consulta. Os novos endereços não recebem geometria nem aparecem como pontos no mapa.
- Endereço e família são salvos em uma transação após a conferência. A família contém número e responsável; pode ser criada ou selecionada entre as famílias da UBS sem residência atual.
- **Desvincular** encerra o vínculo sem excluir o servidor. Quando existem endereços, inclusive arquivados, o gestor precisa escolher outro agente ativo da mesma microrregião. Endereços, vínculos residenciais, visitas e versões permanecem.
- A ficha da família abre as visitas e o histórico existentes. As consultas de cobertura e mapa permanecem disponíveis; o portal operacional concentra as inclusões no novo fluxo. A administração geográfica continua no portal administrativo.

## Segurança e compatibilidade

`/api/registration` exige autenticação, permissões de consulta de imóveis e famílias e escopo de UBS. O agente só consulta seus próprios vínculos e endereços. Inclusões e alterações exigem as permissões correspondentes, CSRF e versões de concorrência. Não há cache de respostas privadas nem persistência de dados cadastrais no navegador.

As migrações `20261021181018_RegistrationHierarchy` e `20261021181821_RegistrationHistoryGeometry` são aditivas. Elas convertem as atribuições anteriores em vínculos explícitos e associam os endereços existentes ao responsável atual. Geometrias anteriores, identificadores, famílias e histórico permanecem. O campo legado de agente principal é mantido para compatibilidade; endereços novos têm responsabilidade explícita. O bairro de endereços antigos só é preenchido automaticamente quando a microrregião possui exatamente um bairro relacionado.

Não executar `STU.FamilyReset` nesta implantação. O utilitário continua restrito à migração histórica `IndependentFamilies` e recusa bases que já possuem o modelo de famílias.

## Validação e implantação

1. Executar `./scripts/verify.ps1`: compilação, testes de unidade e integração, análise estática, testes e build dos dois portais e validação de Compose.
2. Conferir backup restaurável e inventário de identificadores/conteúdo do banco atual. O teste `RegistrationMigrationTests` aplica as migrações sobre dados do modelo anterior em PostGIS descartável e verifica endereços, famílias, visitas e geometrias.
3. Publicar o commit validado e reconstruir somente `app admin api worker gateway`. A API aplica as duas migrações. Preservar banco, Martin, volumes e arquivos operacionais.
4. Comparar o inventário preservado, os vínculos convertidos e as migrações aplicadas. Conferir contêineres, ambos os portais, `/health/live`, `/health/ready` e o fluxo visual.
5. Para contingência, manter imagens anteriores e o backup completo. Retorno de esquema exige manutenção e restauração coordenada; não remover vínculos ou fabricar geometrias para executar `Down` sobre cadastros novos.
