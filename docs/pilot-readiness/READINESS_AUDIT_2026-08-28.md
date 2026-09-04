# Auditoria de prontidão do STU — 2026-08-28

## Parecer

**Aprovado para iniciar a Fase 7 técnica.** O sistema principal, o painel administrativo, a API, o worker, o PostgreSQL/PostGIS, o servidor de mapas, o gateway e o túnel Cloudflare estão no ar e respondendo. Esta aprovação permite iniciar a geração de dados sintéticos, os testes de 300 sessões simultâneas e a avaliação de acessibilidade; ela ainda não equivale à autorização do piloto com dados reais.

## Portas efetivamente utilizadas

| Componente | Porta interna | Porta no host | Exposição | Resultado |
|---|---:|---:|---|---|
| Front-end principal | 80 | — | somente rede Docker, via gateway | ativo |
| Admin global | 80 | — | somente rede Docker, via gateway | ativo |
| Gateway Nginx | 80 | 8088 | `127.0.0.1` | ativo |
| API | 8080 | 8091 | `127.0.0.1` | ativo |
| PostgreSQL/PostGIS | 5432 | 55432 | `127.0.0.1` | ativo e saudável |
| Martin | interna | — | somente rede Docker, via gateway | ativo e saudável |
| Front-end de desenvolvimento | — | 5173/5174 | não iniciado em produção | esperado |

A porta externa da API é 8091 porque o arquivo local `.env` substitui o padrão 8080. A porta 8080 já está ocupada por outro processo Node.js que não pertence ao STU. A porta 3000 também pertence a outro serviço local; o Martin do STU não é publicado diretamente no host. O Cloudflare encaminha `stu.laudaapp.com` e `stu-admin.laudaapp.com` para o gateway na porta 8088.

## Disponibilidade

- Interface principal pública: HTTP 200, aproximadamente 563 ms na amostra.
- Administração pública: HTTP 200, aproximadamente 483 ms.
- Liveness pública: HTTP 200.
- Readiness pública: HTTP 200.
- Gateway local: respostas entre aproximadamente 2 e 14 ms.
- API direta em `127.0.0.1:8091`: HTTP 200 em aproximadamente 32 ms.
- HSTS, CSP e proteção contra MIME sniffing presentes nas respostas públicas.
- Assets versionados dos dois front-ends retornando HTTP 200 com cache imutável.

## Contêineres e recursos

Os sete contêineres do STU estavam ativos. PostgreSQL e Martin reportaram estado saudável. Nenhum contêiner registrou encerramento por falta de memória. O consumo observado foi baixo: aproximadamente 66 MiB no banco, 84 MiB na API, 100 MiB no worker e menos de 10 MiB em cada front-end.

O worker acumula 18 reinicializações históricas. Os logs mostram falhas transitórias de banco e concorrência até 2026-08-26 13:46, durante o período de intervenções na infraestrutura. Não houve nova ocorrência nos logs das últimas 12 horas, e o processo está estável há cerca de dois dias. O heartbeat observado tinha aproximadamente 13 segundos. A causa de cada reinicialização antiga não pode ser comprovada individualmente, portanto o contador deve ser acompanhado durante o teste de carga.

## Banco de dados

- PostgreSQL 18.6 e PostGIS 3.6.4 aceitando conexões.
- Banco com aproximadamente 20 MB e 27 tabelas públicas.
- Migração mais recente: `20260824143856_OperationalMonitoring`.
- Nenhum índice inválido.
- Nenhuma restrição pendente de validação.
- Nenhuma coluna geográfica da aplicação fora do SRID 4326.
- Estado atual: 1 UBS, 4 bairros, 1 microrregião, 0 imóveis, 0 visitas, 2 usuários e 13 registros de auditoria.
- Volume com aproximadamente 922 GB livres na amostra.

## Backup e operação

- Backup semanal habilitado para domingo à meia-noite em `America/Bahia`.
- Retenção configurada para oito arquivos.
- Backup mais recente concluído, com 83.279 bytes e SHA-256 registrado.
- Arquivo de backup presente no volume operacional.
- Exercício de restauração isolada já aprovado na Fase 6.

O próximo ciclo semanal deve ser conferido após a execução de domingo. A cópia externa criptografada continua obrigatória antes da autorização do piloto.

## Build, testes e segurança

- Build .NET Release: zero erros e zero avisos.
- Testes unitários: 15 aprovados.
- Testes de integração: 19 aprovados.
- Testes do front-end principal: 11 aprovados.
- Testes do admin: 4 aprovados.
- Total: 49 testes aprovados.
- Lint e builds de produção dos dois front-ends aprovados.
- Configuração Docker Compose válida.
- NuGet: nenhuma vulnerabilidade conhecida nos projetos ou dependências transitivas.
- npm: zero vulnerabilidades conhecidas.
- Gitleaks: aproximadamente 7,89 MB analisados e nenhum segredo encontrado.
- Imagem da API: zero vulnerabilidades críticas e zero altas no Grype.
- Conteúdo executável da API, worker e dos dois front-ends em execução coincide por hash com as imagens reconstruídas.

A repetição do Grype na imagem web foi interrompida porque o scanner ficou bloqueado no Docker Desktop e gerou I/O excessivo. O contêiner e o volume temporários foram removidos. As versões instaladas de Nginx, OpenSSL e o conteúdo web coincidem com a imagem previamente validada; permanece apenas a observação já registrada sobre a CVE de QUIC do OpenSSL, não aplicável ao caminho HTTP interno do STU.

## Testes funcionais e de proteção

- Emissão pública de token CSRF aprovada.
- Cookie CSRF com `Secure` e `HttpOnly`.
- Login deliberadamente inválido rejeitado com HTTP 401.
- Mapa privado rejeita acesso anônimo com HTTP 401.
- Separação entre portal principal e administração coberta pelos testes de integração.
- Escopo de UBS, logout, arquivamento de conta, CORS, entradas malformadas e ausência de CSRF cobertos pela suíte automatizada.

Não foi utilizado um login operacional real nesta auditoria, para não manusear credenciais existentes. Os fluxos autenticados foram validados com contas sintéticas nos testes de integração.

## Observações para a Fase 7

1. O bundle do MapLibre possui aproximadamente 945 KB antes de gzip. É o principal ponto a medir no teste de carregamento do mapa.
2. O banco ainda não possui imóveis ou visitas reais, portanto o desempenho em escala continua não comprovado.
3. O histórico de reinicializações do worker deve permanecer visível durante o teste de carga.
4. As portas 8080 e 3000 são ocupadas por outros projetos; os scripts da Fase 7 devem usar o gateway 8088 e a API STU em 8091 quando acessada diretamente pelo host.

## Gate da próxima fase

O STU pode iniciar imediatamente a primeira etapa da Fase 7: gerador determinístico de dados sintéticos e cenários k6. O piloto com dados reais somente poderá ser aprovado depois dos testes de 300 sessões simultâneas, WCAG 2.2 AA, ensaio dos três bairros, treinamento, cópia externa criptografada e decisão formal de go/no-go.
