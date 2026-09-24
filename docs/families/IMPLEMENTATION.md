# Planejamento — Famílias e vínculo temporário com imóveis

**Status:** implementação concluída no código em 24/09/2026, a partir do desenho aprovado em 23/09/2026.
**Implantação:** não executada; reinicialização de dados reais depende da aprovação explícita prevista no item 15.12. Consulte [validação](VALIDATION.md) e [procedimento de implantação](DEPLOYMENT.md).

## 1. Objetivo

Introduzir a família como cadastro operacional principal do STU, separando-a do imóvel. A família será identificada somente por seu número e pelo nome do responsável. O imóvel continuará representando endereço e localização territorial.

O relacionamento entre família e imóvel será atual, exclusivo e temporário: uma família pode ocupar um imóvel por vez e um imóvel pode receber uma família por vez, mas essa relação pode mudar sem alterar a identidade da família.

## 2. Correção do modelo atual

O modelo anterior armazenava `FamilyNumber` diretamente em `HealthProperty`, trata o número como identificador do imóvel e exige um número por imóvel. Essa regra foi substituída no código local.

A nova regra de domínio é:

- família e imóvel são cadastros independentes;
- a família é o ponto inicial da consulta operacional;
- o número e o responsável permanecem com a família quando ela muda de imóvel;
- a ligação atual é opcional em ambos os lados, mas, quando existe, é um para um;
- o histórico pode conter vários vínculos sequenciais para a mesma família e para o mesmo imóvel;
- um imóvel sem família permanece cadastrado e disponível para novo vínculo;
- uma família pode ser cadastrada antes de possuir imóvel.

## 3. Escopo funcional

### 3.1 Cadastro de família

A família terá apenas:

- identificador interno;
- UBS;
- número da família;
- nome do responsável;
- estado ativo ou arquivado;
- token de concorrência;
- datas e autoria de criação/alteração.

Não serão cadastrados CPF, CNS, telefone, fotografia, documentos, membros, parentesco, renda, prontuário ou informação clínica.

### 3.2 Regras do cadastro

- Número e responsável são obrigatórios para toda nova família.
- O número terá no máximo 32 caracteres e será normalizado da mesma forma que os identificadores operacionais atuais.
- O número será único dentro da UBS, inclusive entre registros arquivados, para preservar a identidade histórica.
- O responsável será texto simples com até 120 caracteres, espaços externos removidos e grafia preservada.
- Nomes aceitarão acentos, espaços, hífens e apóstrofos sem transformação de caixa.
- Família poderá existir ativa e sem imóvel.
- Arquivamento não excluirá família, vínculos ou visitas.
- Uma família com vínculo atual deverá ter o vínculo encerrado antes de ser arquivada.

## 4. Modelo de vínculo residencial

Criar um registro próprio para o vínculo família–imóvel, contendo no mínimo:

- identificador;
- UBS;
- família;
- imóvel;
- início do vínculo;
- encerramento opcional;
- usuário que criou ou encerrou o vínculo;
- motivo/tipo operacional da alteração, quando aplicável;
- datas de auditoria.

Restrições no banco garantirão:

- no máximo um vínculo atual por família;
- no máximo um vínculo atual por imóvel;
- família e imóvel pertencentes à mesma UBS;
- vínculo apenas com cadastros ativos;
- encerramento posterior ao início;
- impossibilidade de sobrescrever ou apagar o histórico.

Uma mudança de imóvel será transacional: encerra o vínculo anterior e cria o novo na mesma operação. Se qualquer validação falhar, nada será alterado.

## 5. Tela Famílias

Adicionar **Famílias** à navegação principal antes de **Cobertura**.

A tela conterá:

- busca por número da família ou nome do responsável;
- paginação e total de resultados;
- lista de famílias autorizadas para a UBS atual;
- estados `Com imóvel`, `Sem imóvel` e `Arquivada`;
- botão **Cadastrar família**;
- ficha da família selecionada;
- ações de editar, arquivar, reativar, vincular imóvel e alterar imóvel;
- imóvel atual, quando houver;
- histórico de imóveis anteriores;
- visitas e situação de cobertura da família.

O formulário de criação/edição terá somente:

1. **Número da família**;
2. **Nome do responsável**.

O vínculo com imóvel não fará parte desse formulário.

## 6. Fluxo família primeiro

1. O usuário busca ou cadastra a família.
2. Abre a ficha pelo número ou responsável.
3. Se a família estiver sem imóvel, escolhe **Vincular imóvel**.
4. Seleciona um imóvel ativo e sem família ou escolhe **Cadastrar novo imóvel**.
5. Depois do cadastro do imóvel, o sistema retorna à família e conclui o vínculo.
6. Em uma mudança, escolhe **Alterar imóvel**, seleciona o novo imóvel disponível e confirma o impacto.
7. O vínculo anterior é encerrado e o novo é criado atomicamente.
8. O imóvel anterior permanece ativo e sem família.

O cadastro de imóvel deixará de solicitar número da família. A ficha do imóvel exibirá sua família atual e o histórico de famílias que o ocuparam.

## 7. Visitas e cobertura

- A visita passará a pertencer à família.
- A visita também armazenará o imóvel em que ocorreu.
- Registrar visita exigirá que a família possua vínculo atual com um imóvel.
- Mudanças de imóvel não transferirão nem reescreverão visitas antigas.
- A cobertura será calculada pela última visita não arquivada da família.
- Família sem imóvel aparecerá como `Sem imóvel` e ficará fora do cálculo territorial de cobertura até ser vinculada.
- Imóvel sem família continuará visível no mapa e na lista, mas ficará fora dos cálculos de cobertura.
- O mapa continuará espacialmente centrado no imóvel e mostrará os dados da família atualmente vinculada somente para usuários autorizados.

## 8. Busca, painel e indicadores

- A busca geral priorizará número da família e responsável.
- O resultado abrirá primeiro a ficha da família, com acesso ao imóvel atual.
- Os indicadores contarão famílias reais, não números gravados em imóveis.
- Alertas de cobertura serão agregados por família.
- Imóveis sem família terão indicador próprio de pendência de vínculo, sem serem contados como família atrasada.
- Consultas territoriais continuarão permitindo localizar imóveis sem família.

## 9. Permissões e privacidade

Criar permissões específicas:

- `families.view`;
- `families.manage`.

Inicialmente, elas serão atribuídas aos mesmos perfis que hoje consultam e gerenciam imóveis. A API aplicará permissão e recorte de UBS em toda consulta e comando.

O nome do responsável:

- não será incluído em camada pública ou servidor de tiles;
- não será enviado ao Nominatim ou a qualquer serviço externo;
- não será armazenado em cache de runtime da PWA;
- não aparecerá em logs técnicos detalhados;
- poderá aparecer apenas em respostas autenticadas, exportações autorizadas e auditoria estritamente necessária.

## 10. Importação e exportação

A importação de imóveis passará a aceitar:

- `familyNumber`;
- `familyResponsibleName`.

Regras:

- ambos vazios: cria somente o imóvel, sem família;
- ambos preenchidos: cria família, imóvel e vínculo inicial na mesma aprovação;
- apenas um preenchido: linha inválida;
- número familiar já existente: a importação não criará duplicata e exigirá fluxo explícito de vínculo;
- imóvel ou família já vinculados: conflito bloqueante;
- toda prévia exibirá separadamente imóveis, famílias e vínculos a criar.

Exportações apresentarão separadamente dados do imóvel, família atual e vínculo. Formatos que preservam histórico poderão incluir os vínculos encerrados; o formato operacional padrão exportará o vínculo atual.

## 11. Implantação com reinicialização dos dados operacionais

Não haverá migração dos imóveis e números familiares atuais. Antes da exclusão, a implantação deverá apresentar contagens dos alvos e confirmar que está conectada ao ambiente correto.

Serão removidos:

- imóveis;
- visitas vinculadas aos imóveis;
- versões e históricos de imóveis;
- vínculos entre imóveis e etiquetas;
- importações/exportações de imóveis pendentes ou preparadas;
- arquivos temporários dessas operações;
- aprovações de pré-implantação invalidadas por esses dados.

Serão preservados:

- UBS, bairros e microrregiões;
- usuários, funções e permissões existentes;
- etiquetas operacionais;
- regras de cobertura;
- configurações, infraestrutura e registros append-only de auditoria.

A remoção ocorrerá junto com a adoção do novo modelo, evitando período em que a aplicação opere com duas definições de família.

## 12. API planejada

Os nomes finais poderão seguir as convenções existentes, mas a superfície funcional deverá cobrir:

- listar, consultar, criar, editar, arquivar e reativar famílias;
- consultar histórico da família;
- vincular, alterar e encerrar vínculo com imóvel;
- listar imóveis disponíveis;
- consultar histórico de ocupação do imóvel;
- registrar e consultar visitas pela família;
- buscar famílias por número ou responsável;
- expor família atual somente em respostas autenticadas de imóvel/mapa.

Todas as gravações usarão antiforgery, autorização, escopo de UBS, validação no servidor, concorrência otimista e auditoria.

## 13. Tratamento de erros

Mensagens específicas deverão cobrir:

- número familiar já utilizado na UBS;
- família ou imóvel já vinculados;
- família e imóvel de UBS diferentes;
- cadastro arquivado;
- imóvel fora da microrregião válida;
- edição concorrente;
- tentativa de visita sem imóvel atual;
- tentativa de arquivamento com vínculo atual;
- importação parcial ou contraditória.

Nenhum conflito poderá encerrar o vínculo atual antes de validar completamente o novo.

## 14. Testes e critérios de aceite

### Domínio e persistência

- normalização e unicidade do número por UBS;
- validação do responsável;
- vínculos atuais exclusivos em ambos os lados;
- mudanças atômicas e histórico imutável;
- arquivamento e reativação;
- concorrência e restrições de banco.

### API e segurança

- isolamento entre UBS;
- `families.view` e `families.manage`;
- perfis sem permissão;
- busca e paginação;
- vínculo, mudança e desvinculação;
- visita associada à família e ao imóvel da data;
- cobertura seguindo a família;
- importação/exportação;
- ausência do nome em respostas públicas e integrações externas.

### Interface

- navegação Famílias;
- criação e edição com dois campos;
- estados com/sem imóvel e arquivado;
- busca por número e responsável;
- vínculo com imóvel existente;
- cadastro de novo imóvel com retorno à família;
- mudança de imóvel e confirmação de impacto;
- histórico de imóveis e visitas;
- acessibilidade por teclado, foco, rótulos, mensagens e diálogo;
- revisão responsiva e visual com dados sintéticos.

### Verificação final

- testes unitários e de integração;
- testes das interfaces do aplicativo e administração;
- análise estática e builds de produção;
- validação da migração destrutiva em banco descartável;
- conferência das contagens preservadas e removidas;
- execução integral de `scripts/verify.ps1`;
- nenhuma publicação antes da aprovação de todos os gates.

## 15. Sequência de implementação

1. Criar entidades, configurações, restrições e migração do novo modelo.
2. Implementar permissões e políticas.
3. Implementar API de famílias e vínculos.
4. Reorientar visitas e cobertura para a família.
5. Criar a tela Famílias e o fluxo família primeiro.
6. Separar o formulário de imóvel do cadastro familiar.
7. Atualizar busca, painel, mapa e indicadores.
8. Atualizar importação e exportação.
9. Atualizar dados sintéticos, testes e documentação contraditória anterior.
10. Validar a reinicialização dos dados em banco descartável.
11. Executar a suíte completa e revisar visualmente.
12. Somente após aprovação explícita, realizar a implantação e a limpeza autorizada.

## 16. Decisões registradas

| Decisão | Alternativas consideradas | Motivo |
|---|---|---|
| Família é cadastro próprio | Nome no imóvel; referência direta sem entidade | A família mantém identidade ao mudar de imóvel. |
| Família contém apenas número e responsável | Cadastro de pessoas, CPF, foto ou outros campos | Escopo mínimo e privacidade. |
| Tela Famílias é o fluxo principal | Permanecer em Cobertura/Imóveis | A operação começa pela família e depois chega ao imóvel. |
| Família pode existir sem imóvel | Exigir imóvel na criação | Permite cadastrar primeiro e vincular depois. |
| Vínculo atual é um para um | Várias famílias por imóvel; vínculo permanente | Regra operacional confirmada pelo usuário. |
| Vínculo tem histórico | Sobrescrever o imóvel atual | Mudanças não podem apagar relações anteriores. |
| Visitas acompanham a família | Visitas permanecerem apenas no imóvel | Continuidade do acompanhamento após mudança. |
| Imóvel da visita é preservado | Apenas família na visita | Mantém o contexto territorial histórico. |
| Imóvel sem família permanece visível | Arquivar ou ocultar imóvel vazio | O bem territorial continua existindo e pode receber nova família. |
| Permissões próprias de família | Reutilizar somente permissões de imóvel | O responsável é dado pessoal e o módulo é independente. |
| Importação adota o novo modelo | Manter `FamilyNumber` dentro do imóvel | Evita modelos contraditórios. |
| Dados operacionais atuais serão reiniciados | Migração gradual e compatibilidade temporária | Os dados ainda não são definitivos e a simplificação foi autorizada. |

## 17. Não objetivos

- cadastro individual de pessoas ou membros familiares;
- integração automática com e-SUS APS;
- CPF, CNS, telefone, fotografia, documentos ou prontuário;
- múltiplas famílias simultâneas no mesmo imóvel;
- múltiplos imóveis simultâneos para a mesma família;
- geocodificação do nome do responsável;
- cache offline de famílias ou visitas;
- exclusão definitiva durante a operação normal após a implantação.
