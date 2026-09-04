# Pesquisa — imóveis e visitas

## Visão geral

A Fase 4 transforma o território em cadastro operacional: imóveis georreferenciados, identificadores vinculados ao imóvel, tags, visitas estruturadas e alertas de cobertura. Não haverá cadastro de moradores nem informação clínica.

## Regras confirmadas

- Todo imóvel pertence a exatamente uma UBS e uma microrregião.
- Todo imóvel possui número do endereço e exatamente um número de família.
- O número de família é único dentro da UBS e identifica o imóvel, não uma pessoa ou núcleo familiar.
- Trocas de número, microrregião, posição, situação e tags preservam snapshots anteriores.
- Imóveis e visitas são arquivados; não há exclusão operacional definitiva.
- Agentes de saúde editam somente imóveis de microrregiões atribuídas a eles.
- Gerentes e funções personalizadas autorizadas operam em toda a UBS; recepcionistas e médicos consultam toda a UBS.
- A visita usa campos estruturados e observação opcional curta, com proibição explícita de dados pessoais e clínicos.
- Tags são configuradas por gerente e administrador global.
- Cada microrregião pode definir prazo máximo sem visita. A situação é calculada usando a última visita não arquivada.

## Abordagem técnica

- `HealthProperty` armazena endereço, número do imóvel, número da família, situação, geometria e token de concorrência.
- Índice único composto `(HealthUnitId, FamilyNumber)` protege a regra mesmo com requisições concorrentes.
- `PropertyVersion` é append-only e registra o snapshot após criação, edição, arquivamento e restauração.
- `OperationalTag` é escopada à UBS; `PropertyTag` mantém a relação muitos-para-muitos.
- `PropertyVisit` armazena tipo, resultado, dificuldade de acesso, situação observada, data, agente e nota curta.
- `CoverageRule` é única por microrregião e define o número máximo de dias sem visita.
- Pontos e polígonos usam SRID 4326 e índice GiST. A API valida que a geometria esteja coberta pela microrregião.
- A camada de imóveis aparece somente em zoom alto e exibe `número do imóvel · F número da família`.

## Segurança e privacidade

- Escopo da UBS e da microrregião é aplicado no servidor em consultas e comandos.
- A interface não oferece campos de morador, diagnóstico, documento ou telefone.
- Observações têm limite curto, aviso permanente e validação para bloquear padrões evidentes de CPF, telefone e e-mail.
- Respostas autenticadas continuam fora do cache da PWA.
- Alterações produzem auditoria e nunca sobrescrevem o histórico de identificadores.

## Referências

- PostgreSQL recomenda restrições e índices únicos para garantir unicidade no banco: https://www.postgresql.org/docs/17/ddl-constraints.html
- EF Core oferece tokens de concorrência otimista para impedir sobrescritas silenciosas: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- Npgsql traduz geometrias do NetTopologySuite para PostGIS: https://www.npgsql.org/efcore/mapping/nts.html
- MapLibre suporta `minzoom` em camadas e símbolos de texto para exibir imóveis apenas no detalhamento adequado: https://maplibre.org/maplibre-style-spec/layers/

## Fora desta fase

- Moradores, prontuários e dados clínicos;
- Roteirização de visitas;
- Importações em lote e exportações;
- Notificações in-app;
- Aplicativo offline.
