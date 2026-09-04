# Implementação — vínculos e encaixe de limites territoriais

## Fase 1: Modelo e migração

- [x] Adicionar cores validadas às entidades e versões.
- [x] Criar associação muitos-para-muitos entre microrregiões e bairros.
- [x] Migrar os vínculos existentes sem perda.
- [x] Guardar `NeighborhoodIds` e cor nos snapshots.
- [x] Alterar a unicidade do código para UBS + código.

Critério: banco migrado, dados existentes preservados e modelo compilando.

## Fase 2: Regra espacial e API

- [x] Implementar recorte determinístico da geometria enviada contra áreas existentes.
- [x] Aplicar a bairros e microrregiões em criação e edição.
- [x] Validar microrregião contra a união dos bairros selecionados.
- [x] Atualizar contratos, GeoJSON, referência, preview, histórico, auditoria e arquivamento.
- [x] Manter autorização, UBS, concorrência e versionamento.

Critério: a primeira área permanece intacta, a segunda compartilha exatamente sua borda e nenhum interior fica sobreposto.

## Fase 3: Interface

- [x] Trocar seleção única de bairro por lista múltipla acessível.
- [x] Mostrar geometria ajustada na pré-visualização.
- [x] Adicionar seletor de cor para os dois tipos de área.
- [x] Estilizar mapa e cards com a cor cadastrada.
- [x] Explicar que o usuário pode sobrepor levemente o desenho vizinho.

Critério: o fluxo completo funciona no aplicativo principal e no painel global compartilhado.

## Fase 4: Testes, publicação e validação

- [x] Cobrir cores, múltiplos bairros e recorte em testes unitários.
- [x] Cobrir API, persistência, histórico e isolamento em integração.
- [x] Executar build, lint e todos os testes.
- [x] Aplicar migração e publicar API, worker e front-ends.
- [x] Validar banco, mapa local e subdomínios públicos.

Critério: suíte completa aprovada e comportamento confirmado no ambiente publicado.
