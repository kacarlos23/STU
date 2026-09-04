# Revisão funcional — imóveis e servidores

Data: 31/08/2026

## Resultado

- O portal principal passa a ter a seção **Servidores** para a gerência da UBS.
- O gerente visualiza apenas as contas vinculadas à própria UBS.
- O gerente pode cadastrar servidores operacionais, alterar nome e função, redefinir senha temporária, arquivar e reativar contas.
- Contas de gerente e administrador global permanecem protegidas nessa área.
- Funções administrativas não podem ser atribuídas pelo gerente; funções operacionais criadas pelo administrador global continuam disponíveis.
- Toda criação, alteração, redefinição de senha, arquivamento e reativação gera registro de auditoria.
- O cadastro de imóvel agora fica indisponível enquanto a UBS não possuir uma microrregião ativa e exibe uma orientação com acesso direto ao mapa territorial.
- Com uma microrregião ativa, o formulário de imóvel permanece disponível e a localização inicial do mapa usa Teixeira de Freitas, BA.
- A API continua validando que o ponto do imóvel pertence à microrregião selecionada e à UBS da conta.

## Situação dos dados reais

Na validação anterior ao desenvolvimento, a UBS Piloto possuía zero microrregiões ativas, zero imóveis e uma conta ativa. Nenhum dado fictício foi criado na base de produção. Para cadastrar o primeiro imóvel real, é necessário cadastrar ou importar e manter ativa ao menos uma microrregião.

## Verificação automatizada

- 23 testes unitários aprovados.
- 23 testes de integração aprovados.
- 18 testes do portal principal aprovados.
- 8 testes do portal administrativo aprovados.
- Total: 72 testes aprovados.
- Compilação .NET em modo Release: zero erros e zero avisos.
- Lint e builds dos dois portais aprovados.
- Configurações Docker principal e de piloto validadas.

## Regras de segurança verificadas

- Isolamento por UBS aplicado no servidor, sem depender de filtros da tela.
- Alteração da própria conta pela tela gerencial bloqueada.
- Contas gerenciais e globais protegidas.
- Senha temporária aleatória exibida apenas após criação ou redefinição e troca obrigatória no próximo login.
- Operações de escrita protegidas contra requisições forjadas e sujeitas ao limite de requisições da API.

