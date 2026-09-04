# Ambiente sintético do piloto

## Objetivo

Criar uma base repetível para medir o STU na escala inicial sem copiar qualquer dado operacional. O ambiente usa serviços e volumes próprios e não compartilha o banco da aplicação publicada.

## Escala fixa

- 1 UBS sintética com carga e 1 UBS vazia para validar isolamento;
- 3 bairros contíguos em Teixeira de Freitas, Bahia;
- 20 microrregiões, incluindo limites que atravessam mais de um bairro;
- 100 contas: 50 agentes, 20 recepcionistas, 20 médicos e 10 gerentes; uma das contas de recepção pertence exclusivamente à UBS vazia de isolamento;
- 3.000 imóveis, distribuídos igualmente entre as microrregiões;
- 4.500 visitas estruturadas;
- 6 tags operacionais e vínculos distribuídos;
- 20 regras de cobertura;
- 100 notificações.

Nenhum nome de morador, dado clínico ou endereço real é usado. As coordenadas e os endereços são sintéticos e seguem uma grade determinística próxima à área urbana apenas para reproduzir volume e comportamento espacial.

## Isolamento

- Banco: `stu_load_pilot` no contêiner `pilot-database`.
- PostgreSQL no host: `127.0.0.1:55433`.
- API do piloto: `127.0.0.1:8092`.
- Volume de banco e volume operacional exclusivos.
- Senhas guardadas apenas nas variáveis de usuário `STU_PILOT_POSTGRES_PASSWORD`, `STU_PILOT_ACCOUNT_PASSWORD` e `STU_PILOT_DB_CONNECTION`.

O gerador encerra com erro se o banco não terminar em `_pilot`, se o host não for local ou o serviço Docker esperado, ou se a conexão apontar para a porta produtiva `55432`.

## Operação

```powershell
.\scripts\pilot-data.ps1 configure
.\scripts\pilot-data.ps1 start
.\scripts\pilot-data.ps1 seed
.\scripts\pilot-data.ps1 status
```

Para remover somente os dados do ambiente piloto:

```powershell
.\scripts\pilot-data.ps1 clean
```

Para parar os serviços preservando o volume:

```powershell
.\scripts\pilot-data.ps1 stop
```

O manifesto gerado fica em `data/pilot/manifest.json`, uma pasta ignorada pelo Git. Ele registra contagens, duração, tamanho do banco e nomes das contas, mas nunca inclui a senha.

## Repetibilidade

Uma nova execução de `seed` é recusada enquanto a UBS sintética existir. O ciclo suportado é `clean`, seguido de `seed`. Valores operacionais, distribuição, grade espacial e nomes de conta permanecem iguais entre execuções; identificadores técnicos, hashes de senha e horários internos podem mudar.
