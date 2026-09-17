# Endereço sugerido pelo mapa — 10/09/2026

## Funcionamento

- Clicar no mapa ou terminar de arrastar o marcador inicia uma consulta após 600 ms sem novos ajustes, desde que o ponto esteja dentro da microrregião selecionada.
- Logradouro e CEP vazios recebem a sugestão. Campos preenchidos não são sobrescritos automaticamente, inclusive quando digitados enquanto a consulta está em andamento.
- O botão «Usar endereço sugerido» permite substituir os campos retornados, mediante confirmação. Número da casa, número da família, complemento e vínculos territoriais não são alterados.
- Respostas de pontos anteriores são ignoradas. Apenas abrir um imóvel existente não dispara consulta. A busca pode ser desmarcada e pode ser repetida manualmente.
- Falhas, ausência de endereço e limite do provedor são apresentados sem impedir o cadastro manual. O resultado pode corresponder a uma rua vizinha: é necessário conferir antes de salvar.

## Servidor e privacidade

- Endpoint autenticado `/api/properties/address-suggestion`: exige permissão de cadastro de imóveis, UBS autorizada, microrregião ativa e atribuída quando o usuário é agente, além do ponto dentro do limite.
- Apenas latitude e longitude são transmitidas ao provedor, sem identificação do usuário, imóvel, família ou UBS. Cabeçalho de identificação do aplicativo, idioma e parâmetros de consulta acompanham a requisição.
- Serviço público Nominatim, baseado no OpenStreetMap, sem contratação de API paga nesta entrega. Atribuição e política de uso estão na interface.
- Limite global por processo: intervalo mínimo de 1,1 segundo entre consultas externas, sem fila ilimitada ou repetição automática. Bloqueio temporário após 403/429 do provedor.
- Cache limitado a 2.000 resultados públicos em memória no servidor, com expiração de 24 horas. Nenhuma resposta autenticada é persistida no navegador/PWA. Logs automáticos do cliente HTTP de geocodificação foram desativados.
- Timeout de 8 segundos, limite de resposta de 64 KiB e redirecionamento externo desativado.

## Configuração operacional

No ambiente do Docker Compose:

- `STU_GEOCODING_ENABLED=false`: desativa a consulta externa, mantendo o cadastro manual.
- `STU_GEOCODING_BASE_URL`: endereço HTTPS de um provedor compatível com Nominatim, com barra final. Padrão: `https://nominatim.openstreetmap.org/`.

Aplicar mudanças de configuração recriando somente a API; não é necessário alterar código. Antes de escalar a API para múltiplas instâncias ou ampliar significativamente o uso, adotar limitação compartilhada e avaliar provedor contratado ou hospedagem própria. O limite do Nominatim vale para a aplicação inteira, não para cada usuário ou servidor.

## Validações

- Verificação completa: 23 testes unitários, 45 de integração/servidor, 42 do aplicativo e 8 do administrador: **118 aprovados**.
- Compilação .NET sem avisos/erros, análise estática das interfaces e builds de produção aprovados. Permanece o aviso conhecido de tamanho do pacote cartográfico.
- Testes cobrem isolamento por UBS, permissão de leitura insuficiente, agente não atribuído, coordenadas inválidas e fora da área, cache, limitação, provedor indisponível, resposta malformada, ausência de endereço, preservação dos campos, confirmação de substituição, desativação e descarte de respostas antigas.
- Validação no navegador integrado com dados fictícios: ponto marcado, logradouro e CEP preenchidos, números da casa e família preservados em branco.
- Uma consulta real ao provedor em ponto de demonstração em Teixeira de Freitas respondeu HTTP 200, com Rua José Garcia e CEP 45994-009. Isso comprova disponibilidade naquele momento, não a precisão de todos os endereços.
- Nenhum imóvel ou visita real foi criado/alterado durante os testes; nenhuma migração de banco necessária.

Referências: [API de consulta reversa](https://nominatim.org/release-docs/latest/api/Reverse/) e [política de utilização](https://operations.osmfoundation.org/policies/nominatim/).

## Publicação

API e interfaces publicadas. Aplicativo e administrador responderam HTTP 200 com as novas entradas `index-B9OndUyK.js` e `index-D3hImbUK.js`. Prontidão pública da API: HTTP 200; novo endpoint sem autenticação: HTTP 401. Dados existentes preservados.

## Consumo observado

As leituras de uso da conta nesta implementação mostraram 49% → 93% da janela de cinco horas e 50% → 57% da janela semanal. São percentuais compartilhados da conta, não uma cobrança isolada desta tarefa nem uma conversão em tokens. Saldo de créditos adicionais permaneceu zero; os três créditos de redefinição permaneceram disponíveis, sem uso. A ferramenta de consumo não informa tokens ou custo monetário por tarefa.
