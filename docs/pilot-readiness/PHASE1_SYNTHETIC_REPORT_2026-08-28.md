# Relatório da Fase 7.1 — ambiente sintético

## Resultado

**Aprovado.** O STU possui agora um ambiente isolado e repetível na escala planejada para o piloto. A carga não utiliza dados pessoais, endereços reais ou qualquer registro da base publicada.

## Ambiente

| Componente | Endereço | Estado |
|---|---|---|
| PostgreSQL/PostGIS piloto | `127.0.0.1:55433` | saudável |
| API piloto | `127.0.0.1:8092` | pronta |
| Worker piloto | rede Docker isolada | ativo |
| Banco | `stu_load_pilot` | protegido pelo sufixo `_pilot` |

API, worker, banco e volumes pertencem ao projeto Docker `stu-pilot`. A aplicação publicada continua usando o projeto `stu`, a porta 55432 e o banco `stu`.

## Contagens finais

| Registro | Quantidade |
|---|---:|
| UBS | 2 (uma com carga e uma vazia para isolamento) |
| Bairros | 3 |
| Microrregiões | 20 |
| Imóveis | 3.000 |
| Imóveis ativos | 2.850 |
| Imóveis em rascunho | 150 |
| Visitas | 4.500 |
| Contas | 100 |
| Tags | 6 |
| Vínculos imóvel-tag | 1.300 |
| Notificações | 100 |
| Regras de cobertura | 20 |

Distribuição das contas: 50 agentes, 20 recepcionistas, 20 médicos e 10 gerentes.

## Integridade territorial

- 8 microrregiões atravessam e pertencem a mais de um bairro.
- Nenhum interior de microrregião se sobrepõe a outro.
- Nenhum imóvel ficou fora do limite da sua microrregião.
- Nenhuma geometria inválida ou com SRID diferente de 4326.
- Nenhum número de família duplicado dentro da UBS.
- O mapa territorial autenticado retornou 23 áreas.
- O mapa de imóveis de um agente retornou HTTP 200.

## Repetibilidade e recuperação

A primeira carga levou aproximadamente 8,21 segundos. Em seguida, API e worker foram interrompidos, a limpeza protegida zerou todas as contagens do banco piloto e uma segunda carga produziu as mesmas quantidades em aproximadamente 7,82 segundos.

O manifesto da execução fica em `data/pilot/manifest.json` e não contém senha. A base final ocupou aproximadamente 25 MB.

## Isolamento e segurança

- O gerador recusa bancos sem o sufixo `_pilot`.
- Hosts remotos são recusados.
- A porta produtiva 55432 é recusada explicitamente.
- As senhas estão em variáveis do perfil do usuário e não no repositório.
- A base produtiva foi consultada após a carga: nenhum registro com os marcadores sintéticos foi encontrado.
- Os três serviços do piloto permanecem ativos, sem reinicializações e sem padrão de erro nos logs após a correção do contêiner.

## Verificação do projeto

- Build .NET Release: zero erros e zero avisos.
- Testes unitários: 23 aprovados.
- Testes de integração: 20 aprovados.
- Testes do aplicativo: 11 aprovados.
- Testes administrativos: 4 aprovados.
- Total: 58 testes aprovados.
- Lint, builds web e as duas configurações Docker Compose aprovados.

## Próximo gate

A Fase 7.2 pode começar. O próximo trabalho é executar um smoke test pequeno e, depois, uma rampa k6 até 300 sessões simultâneas, preservando uma linha de base antes de qualquer ajuste. Os limites permanecem: menos de 1% de falhas, p95 abaixo de 2 segundos para fluxos comuns e p95 abaixo de 3 segundos para mapas.

Referências técnicas: [cenários k6](https://grafana.com/docs/k6/latest/using-k6/scenarios/), [thresholds k6](https://grafana.com/docs/k6/latest/using-k6/thresholds/), [parametrização de dados k6](https://grafana.com/docs/k6/latest/examples/data-parameterization/) e [ST_MakePoint/PostGIS](https://postgis.net/docs/ST_MakePoint.html).
