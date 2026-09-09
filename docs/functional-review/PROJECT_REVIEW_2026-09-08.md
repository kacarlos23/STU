# Revisão geral do STU — 08/09/2026

Conclusão e conferência pós-publicação: 09/09/2026.

## Parecer

O sistema está operacional para continuar o cadastro e a preparação do piloto. A próxima etapa do plano é o ensaio controlado com os dados reais dos três bairros, seguido de aceitação dos usuários. Ainda não há elementos para registrar a liberação operacional do piloto.

Esta revisão aplicou correções de integridade territorial e de validação da pré-implantação antes de avançar. Nenhum cadastro operacional foi criado, editado ou arquivado para simular o atendimento dos critérios de liberação.

## O que já está implementado

| Etapa | Implementação conferida | Situação |
|---|---|---|
| Fundação | API .NET, dois portais React, PostGIS, Docker, gateway e GitHub Actions | Operacional |
| Acesso e servidores | Login individual, senha temporária, isolamento por UBS, perfis, permissões e gestão de servidores | Coberto pela suíte de integração |
| Território | Desenho/edição de vértices, importação OSM, cores, múltiplos bairros, ajuste de sobreposição, histórico e arquivamento recuperável | Correções de integridade aplicadas nesta revisão |
| Imóveis e visitas | Imóvel com número da casa/família, geometria, visitas estruturadas, etiquetas, cobertura e histórico | Correções de restauração e cobertura aplicadas |
| Operação | Indicadores, notificações, importação aprovada em etapas e exportações CSV/GeoJSON/KML/GeoPackage | Implementado; os fluxos de API continuam cobertos pelos testes |
| Backup e monitoramento | Agenda semanal, execução manual, retenção, assinatura de integridade, disponibilidade e heartbeat | Serviços saudáveis e backup recente validado |
| Piloto | Ensaio sintético, relatório histórico de carga, verificações de acessibilidade, pré-implantação e decisão de liberação | Preparação técnica disponível; aceitação operacional pendente |

Os testes de 300 sessões e a restauração completa têm relatórios anteriores no projeto. Não foram repetidos nesta revisão; os resultados históricos não constituem uma nova medição de carga ou de recuperação em 08/09.

## Erros corrigidos

1. **Pré-visualização territorial sem impacto real.** O endpoint devolvia listas vazias mesmo com um imóvel fora do contorno proposto. Agora informa a quantidade, os impedimentos e até 100 imóveis afetados, identificados por endereço e número de família. Perfis sem permissão de consultar imóveis recebem somente contagens.
2. **Redução de microrregião invalidava imóveis.** O salvamento direto passou a revalidar o limite contra os imóveis ativos vinculados. Alterações inconsistentes retornam conflito e preservam os registros, a versão e a auditoria. Imóveis exatamente na borda continuam válidos.
3. **Edição de bairro podia invalidar microrregiões.** Alterações de contorno verificam as microrregiões ativas antes de salvar. Quando os bairros relacionados mudam sem invalidar os limites, os vínculos são recalculados e versionados no mesmo salvamento.
4. **Reativação não revalidava dependências territoriais.** A restauração de microrregiões verifica agente ativo da mesma UBS, UBS ativa, limites atuais dos bairros e imóveis vinculados. A restauração de imóveis verifica uma microrregião ativa da mesma UBS que cubra a geometria. A edição de áreas arquivadas continua disponível.
5. **Mudança de UBS podia deixar vínculos inconsistentes.** A edição simples de imóvel não aceita troca de UBS, e a mudança de UBS de uma microrregião com imóveis vinculados é bloqueada. A transferência territorial completa exige um fluxo próprio para visitas, etiquetas e demais dependências; não há transferência automática nesta revisão.
6. **Validação de edição antiga.** O preview agora rejeita uma versão de microrregião desatualizada.
7. **Aprovação de pré-implantação baseada apenas em contagens.** A assinatura do estado passa a incluir identidades, versões dos cadastros, vínculos e atribuições. Alterar um número da casa, por exemplo, invalida a assinatura mesmo mantendo a mesma quantidade de imóveis. Contas que deixaram de ser agentes ativos da UBS não satisfazem a regra de atribuição.
8. **Quaisquer três bairros satisfaziam o piloto.** A conferência agora verifica os nomes aprovados para este piloto: Luiz Eduardo Magalhães, Nova Teixeira e Redenção. Também exige imóveis cadastrados para o ensaio. Esse critério corresponde ao piloto inicial; a expansão para outras UBS precisará parametrizar seu próprio território de implantação.
9. **Imóveis nunca visitados apareciam como atrasados.** A lista e o mapa usavam uma data mínima em vez de ausência de visita. A correção mantém `neverVisited` e data nula, de forma consistente com o detalhe do imóvel.
10. **Avisos do mapa fora do formulário.** Erros e resultados da validação agora aparecem dentro do editor. O botão Salvar permanece indisponível quando o impacto está bloqueado.
11. **Dependência vulnerável.** `fast-uri` foi atualizado de 3.1.5 para 3.1.7 no lockfile, sem atualização geral de dependências. A auditoria npm passou a indicar zero vulnerabilidades conhecidas; a consulta NuGet também não reportou pacotes vulneráveis.

A validação de cobertura considera o interior e a borda, conforme a semântica de [ST_Covers](https://postgis.net/docs/ST_Covers.html). A dependência atualizada constava no alerta [GHSA-5jgf-p345-68v8](https://github.com/advisories/GHSA-5jgf-p345-68v8) apresentado pelo npm.

## Verificações realizadas

- Baseline anterior às alterações: 82 testes aprovados, compilação, análise estática, builds e configurações Docker válidos.
- Seis cenários novos reproduziram as falhas antes da correção; outro teste reproduziu o erro `neverVisited`/`overdue`.
- A suíte foi ampliada com testes de integridade territorial, reativação, escopo administrativo, privacidade do preview, consistência de cobertura, nomes do piloto e mudança de assinatura cadastral.
- Resultado final: **95 testes aprovados** — 23 unitários .NET, 34 de integração, 30 do portal principal e 8 do portal administrativo. Compilação .NET sem erros ou avisos; análise estática, builds dos dois portais e validação das configurações Docker aprovados.
- Duas verificações da interface cobrem a apresentação dos imóveis afetados e a indicação de impedimentos.
- Banco: nenhum índice inválido, nenhuma restrição pendente, nenhum imóvel com UBS diferente da microrregião e nenhuma sobreposição de interior entre as microrregiões ativas.
- Backup mais recente: 06/09/2026, 00h no horário local; assinatura SHA-256 conferida contra o arquivo e catálogo PostgreSQL legível, com 192 linhas na listagem. Isto verifica integridade e legibilidade, não substitui um novo ensaio completo de restauração.
- Agenda semanal: domingo, 00h, `America/Bahia`.
- Aplicação, administração e readiness público/local responderam HTTP 200. Consulta anônima ao mapa privado respondeu HTTP 401.
- API, worker e gateway não apresentaram mensagens de falha na amostra recente de logs inspecionada.

| Serviço | Porta no Windows | Exposição |
|---|---:|---|
| Gateway dos dois portais | 8088 | 127.0.0.1, publicado pelo túnel HTTPS |
| API | 8091 | 127.0.0.1 |
| PostgreSQL/PostGIS | 55432 | 127.0.0.1 |
| Front-ends e Martin | Interna | Rede Docker |

O servidor continua sendo o computador Windows com os serviços em Docker, conforme a decisão operacional vigente. Não foi alterada a infraestrutura de outros projetos.

## Pendências para a próxima etapa

1. **Conferir os bairros atendidos.** Luiz Eduardo Magalhães está cadastrado, mas ainda sem microrregião ativa vinculada. As microrregiões atuais relacionam Nova Teixeira, Redenção e Jardim Liberdade. Os contornos reais precisam ser conferidos pelo responsável; não é correto substituir um nome ou vínculo automaticamente.
2. **Atribuir responsáveis.** Duas das três microrregiões ativas estão sem agente.
3. **Completar os cadastros e ensaiar a rotina.** Há um imóvel e nenhuma visita registrada. O conjunto ainda não representa o território completo nem demonstra o fluxo diário com os dados reais.
4. **Cadastrar participantes do ensaio.** Existem uma conta de administrador global, uma de gerente e uma de agente. Faltam os participantes de recepção e médico.
5. **Executar a cópia externa criptografada.** O destino em outro computador Windows foi escolhido conceitualmente, mas a transferência automática e a comprovação de recebimento continuam pendentes, conforme decisão de tratar essa infraestrutura posteriormente.
6. **Concluir treinamento e aceitação.** Falta a sessão supervisionada de acessibilidade, a aceitação por função e a identificação dos responsáveis por suporte, incidentes e restauração.
7. **Registrar a decisão de liberação.** Somente depois das evidências acima, revisar Pré-implantação e Liberação do piloto no sistema.

Melhorias futuras já separadas do MVP no plano: sugestões de endereço/CEP, rota sugerida, edição móvel/offline dedicada e autenticação institucional/MFA. A hospedagem ou configuração efetiva do mapa-base local e a transferência territorial completa entre UBS também exigem implementação/configuração própria antes da expansão; não foram declaradas concluídas por esta revisão.

## Limites da revisão

Esta é uma revisão funcional e técnica com testes automatizados e verificações do ambiente. Não equivale a uma auditoria exaustiva de segurança, comprovação de 99,5% de disponibilidade mensal ou aceitação humana de todos os fluxos. O aviso de tamanho do pacote MapLibre continua presente no build e deve ser acompanhado nas medições de carregamento.

A conferência visual pós-publicação alcançou a tela de entrada do portal principal, sem sessão autenticada disponível no navegador integrado. Os fluxos autenticados foram verificados por testes automatizados em banco isolado; não foram percorridos novamente com uma conta real no navegador de produção.

## Publicação

API, portal principal, portal administrativo e gateway foram reconstruídos e publicados, preservando o banco e seus volumes. Em 09/09/2026, os sete serviços permaneciam ativos, com PostgreSQL e Martin saudáveis, e os portais, a API e o gateway respondiam HTTP 200. O mapa privado sem autenticação respondeu HTTP 401.

Os arquivos territoriais corrigidos foram conferidos diretamente nos dois endereços públicos, ambos com HTTP 200 e contendo a nova apresentação de impedimentos: `territory-BepWDdVH.js` no portal principal e `territory-CDa8bXG5.js` na administração. Não foi necessária migração de banco de dados.
