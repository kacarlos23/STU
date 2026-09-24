# Validação da implementação de famílias

Data: 24/09/2026. Ambiente: checkout local do STU, testes com PostgreSQL 18/PostGIS 3.6 descartável e interface local com dados sintéticos. Nenhuma publicação ou limpeza de dados reais foi executada.

## Comportamento entregue

- Cadastro independente com número e responsável, número normalizado e reservado por UBS inclusive após arquivamento, versões e autoria.
- Navegação **Famílias** antes de **Cobertura**, busca por número/responsável, paginação, filtros, ficha, criação, edição, arquivamento e reativação.
- Vínculo residencial opcional e exclusivo, mudança atômica, encerramento explícito e histórico protegido no banco. Novo imóvel pode ser cadastrado a partir da família e vinculado ao retornar à ficha.
- Imóvel sem campo de número familiar; sua ficha apresenta família atual e histórico de ocupação. Imóveis sem família continuam visíveis e fora da cobertura.
- Visitas associadas à família e ao imóvel histórico; cobertura acompanha a última visita ativa da família após mudança. Formulário de visita antigo não registra silenciosamente no novo imóvel.
- Painel conta famílias reais e informa famílias sem imóvel e imóveis sem família. Busca geral abre o módulo familiar.
- API exige autorização, escopo territorial, antiforgery e versões nos comandos familiares. Dados familiares são omitidos para perfis sem `families.view`; respostas privadas usam `no-store`, sem cache de runtime da PWA.
- Importação exige os dois dados familiares juntos ou ambos vazios, prévia separa as três contagens e aprovação grava o lote atomicamente. Exportação separa identificadores de imóvel, família e vínculo atual; CSV/GeoJSON também identificam o imóvel da última visita.
- Ferramenta de reinicialização com prévia, identificação do ambiente, fingerprint, manifesto e conferência dos dados preservados. Aprovações anteriores são invalidadas logicamente, preservando a auditoria.

## Evidências automatizadas

Os testes familiares cobrem normalização, limites, caracteres de controle, unicidade com arquivadas, versões, busca/paginação, UBS, perfis, antiforgery, resposta anônima, isolamento do agente, concorrência de vínculo, mudança sem perda do vínculo anterior, cobertura após mudança, visitas com endereço preservado e restrições diretamente no banco.

O ensaio de migração parte do esquema anterior com imóvel, visita, versão, etiqueta, trabalho e arquivo sintéticos. Comprova o bloqueio sem autorização, recusa do banco incorreto, aplicação pela ferramenta real, exclusão do arquivo referenciado e dos intermediários de preparação/exportação interrompidos, preservação dos cadastros, cópia das permissões e auditoria imutável.

Os testes de importação/exportação executam o worker real: dados familiares parciais são recusados, conflito surgido depois da prévia reverte todo o lote, contagens são separadas e exportações CSV, GeoJSON e KML preservam a relação atual. A repetição de um trabalho falho reconfirma também a permissão de imóveis correspondente, impedindo reativar importação ou exportação com acessos incompletos. GeoPackage utiliza a mesma preparação GeoJSON e o conversor GDAL existente; a conversão binária não foi executada neste ensaio local.

Os testes de interface incluem formulário de dois campos, busca/filtros, vínculo existente, cadastro de novo imóvel com retorno à família, mensagem de impacto na mudança, conflito mantendo a residência anterior, edição concorrente sem apagar campos, proteção de saída e perfil de consulta.

## Revisão visual

Inspeção em Chromium local, desktop 1440 × 1080 e celular 390 × 844. Nenhum erro de execução observado; largura do documento móvel de 390 px, sem transbordamento horizontal. Após ajuste do contraste, a ficha móvel não apresentou violações na varredura axe WCAG A/AA/2.1 AA. Essa varredura não substitui uma auditoria completa de acessibilidade.

O harness `workflow-visual-test.html?view=families` funciona somente em desenvolvimento/localhost, usa nomes fictícios e bloqueia gravações de API. Evidências:

- [Ficha com imóvel atual, histórico e visita anterior à mudança](evidence/families-desktop.png).
- [Formulário com número e responsável](evidence/family-form.png).
- [Ficha e navegação no celular](evidence/families-mobile.png).

## Gate do projeto

`scripts/verify.ps1` concluído com sucesso:

| Verificação | Resultado |
|---|---|
| Compilação .NET Release | Aprovada, zero avisos e erros |
| Testes unitários | 31 aprovados |
| Testes de integração, incluindo migração descartável | 55 aprovados |
| Testes do aplicativo | 52 aprovados |
| Testes da administração | 8 aprovados |
| Análise estática das interfaces | Aprovada |
| Builds de produção do aplicativo e administração | Aprovados |
| Configuração Docker principal e piloto | Aprovada |

O teste da ferramenta de reinicialização foi repetido após ampliar a conferência dos arquivos intermediários e estabilizar a janela do processo e do comando de migração; passou com o utilitário real.

Total: **146 testes aprovados**, nenhum ignorado. O Vite mantém o aviso de tamanho do pacote cartográfico acima de 500 kB; isso não impediu os builds. Logs locais ficam em `logs/families-verify.log` e não são versionados.

## Limite da entrega

A entrega implementa o plano no código local. Implantação e reinicialização reais seguem o [procedimento preparado](DEPLOYMENT.md) e dependem da aprovação explícita prevista no item 15.12 do plano. O aceite operacional do piloto continua separado da validação técnica.
