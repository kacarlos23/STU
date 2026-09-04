# Procedimentos operacionais do piloto STU

## Ambiente provisório

O piloto será executado em um computador Windows da UBS que também é utilizado pela gerente. Esta topologia é temporária. O computador deve permanecer ligado, sem suspensão automática, e os serviços do STU devem iniciar com o Windows. A expansão para várias UBS exige reavaliação da infraestrutura.

## Suporte

1. Registrar data, horário, usuário, UBS, tela e mensagem apresentada.
2. Verificar o painel de monitoramento antes de reiniciar qualquer serviço.
3. Confirmar se o problema afeta uma conta, uma UBS ou toda a plataforma.
4. Preservar registros, capturas e arquivos relacionados; nunca apagar evidências.
5. Encaminhar ao responsável por suporte registrado no portão de liberação.

## Incidente de segurança ou indisponibilidade

1. Interromper somente a operação afetada, evitando ampliar o impacto.
2. Avisar imediatamente a gerência e o administrador global.
3. Não excluir contas, auditorias, imóveis, visitas ou arquivos envolvidos.
4. Registrar início, alcance, decisões, responsáveis e resultado.
5. Redefinir credenciais somente quando necessário e por fluxo administrativo auditado.
6. Autorizar o retorno após verificar API, banco, worker, armazenamento e acesso por função.

## Backup externo

1. O servidor Windows da UBS gera a cópia local semanal.
2. O arquivo deve ser criptografado antes da transferência.
3. A segunda cópia ficará no computador externo confirmado pelo responsável do projeto.
4. A comunicação deve ocorrer por rede privada; banco, compartilhamento do Windows e portas administrativas não devem ser publicados diretamente na internet.
5. Se o computador externo estiver desligado, a transferência deve permanecer pendente e ser repetida depois.
6. O painel deve registrar data, tamanho, SHA-256 e resultado, sem exibir senha ou chave.

## Restauração e reversão

1. Interromper novas gravações e preservar uma cópia do estado atual.
2. Escolher um backup concluído e conferir seu SHA-256.
3. Restaurar primeiro em ambiente isolado.
4. Validar autenticação, UBS, funções, contagens, geometrias, imóveis, visitas e auditoria.
5. Documentar diferenças e obter autorização do responsável pela restauração.
6. Somente então substituir o ambiente afetado ou retornar a operação.

## Critérios para liberar o piloto

- capacidade e acessibilidade técnica aprovadas;
- pré-implantação aprovada para o estado territorial atual;
- cópia externa criptografada comprovada;
- backup recente, worker e armazenamento saudáveis;
- treinamento e aceitação assistida realizados;
- nenhum defeito crítico ou alto aberto;
- responsáveis por suporte, incidente e restauração identificados;
- decisão final registrada na auditoria.
