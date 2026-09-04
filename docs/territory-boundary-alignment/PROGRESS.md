# Progresso — vínculos e encaixe de limites territoriais

## Status: concluída e publicada em 2026-08-28

### Fase 1 — Modelo e migração

- [x] Modelo atualizado
- [x] Migração criada, revisada e aplicada sem perda dos vínculos anteriores

### Fase 2 — Regra espacial e API

- [x] Ajuste espacial implementado
- [x] API atualizada

### Fase 3 — Interface

- [x] Seleção de vários bairros
- [x] Cores configuráveis
- [x] Pré-visualização ajustada

### Fase 4 — Validação

- [x] Testes automatizados: 17 de unidade, 20 de integração e 15 de interface
- [x] Publicação de API, worker, aplicativo e painel administrativo
- [x] Verificação pública dos dois subdomínios e da prontidão da API

### Evidências da publicação

- Migração ativa: `20260828142938_TerritoryBoundaryAlignment`.
- Quatro bairros, uma microrregião e um vínculo anterior preservados no banco.
- Nenhuma microrregião sem bairro e nenhuma cor inválida após a migração.
- Serviços publicados sem reinicializações ou erros registrados.
- Backup pré-migração validado por catálogo e SHA-256.

## Decisões

- A geometria salva ou editada é recortada; outras áreas nunca são alteradas implicitamente.
- O recorte ocorre entre áreas do mesmo nível.
- Microrregiões têm código único dentro da UBS.
- Snapshots guardam vínculos e cores.
