# Implantação do modelo de famílias

Preparado em 24/09/2026. Este procedimento **não foi executado em produção**. A implementação está no checkout local e a limpeza foi ensaiada somente em PostgreSQL/PostGIS descartável.

O item 15.12 do [plano aprovado](IMPLEMENTATION.md) exige aprovação explícita antes da publicação e da reinicialização real. O funcionamento local e os testes não substituem o aceite operacional do piloto.

## Escopo da reinicialização

A migração `20260923203301_IndependentFamilies` remove imóveis, versões, visitas, relações imóvel–etiqueta e trabalhos de importação/exportação de imóveis. Os arquivos de origem, preparação e resultado referenciados por esses trabalhos, incluindo intermediários com o identificador do trabalho deixados por interrupções, são removidos pela ferramenta após o commit. Isso inclui resultados antigos, que deixam de corresponder ao cadastro reiniciado. Não há conversão dos antigos números em famílias.

UBS, bairros, microrregiões e seus vínculos, identidades, perfis, etiquetas, regras de cobertura e configurações permanecem. As permissões existentes são preservadas e `families.view`/`families.manage` são adicionadas aos perfis que já possuem as respectivas permissões de imóveis. Auditorias anteriores permanecem; o evento `FamiliesReset` invalida logicamente as aprovações de pré-implantação e liberação anteriores. Auditorias passam a recusar alteração e exclusão também no banco.

## Preparação e prévia

1. Aprovar a implantação com o responsável pelo ambiente; registrar banco, diretório operacional e janela de manutenção.
2. Gerar e conferir backup do banco e dos arquivos operacionais. O retorno ao modelo anterior exige restauração coordenada do backup e da versão anterior da aplicação.
3. Parar API e worker antes da prévia definitiva e mantê-los parados até concluir as verificações. Não permitir gravações concorrentes no banco ou no diretório de operações.
4. Compilar o checkout validado. Disponibilizar a conexão correta pela variável `STU_FAMILY_RESET_CONNECTION`, usando o mecanismo seguro do ambiente; não gravar credenciais na documentação, no histórico de comandos ou no repositório.
5. Executar a prévia somente leitura, informando o caminho absoluto que corresponde a `Operations:StoragePath` naquele ambiente:

```powershell
dotnet run --project tools/STU.FamilyReset --configuration Release --no-build -- --storage '<diretorio-operacional-absoluto>'
```

A saída informa host, porta, banco, usuário, diretório, contagens removidas/preservadas, nomes e hashes dos arquivos e um `fingerprint`. A senha não é exibida. A ferramenta exige a migração imediatamente anterior (`20260902201258_ActiveMicroregionIdentifiers`) e recusa execução se o modelo familiar já estiver instalado.

Conferir esses dados com o ambiente aprovado. O fingerprint considera o conteúdo dos registros e arquivos, não apenas as contagens: se algo mudar entre a prévia e a confirmação, gerar e revisar outra prévia.

## Aplicação após aprovação

```powershell
dotnet run --project tools/STU.FamilyReset --configuration Release --no-build -- --storage '<mesmo-diretorio-absoluto>' --apply --database '<nome-exato-do-banco>' --confirm '<fingerprint-revisado>' --maintenance-confirmed --manifest '<caminho-absoluto-novo-para-manifesto.json>'
```

O manifesto deve ser um arquivo novo em local seguro, fora dos arquivos operacionais que serão removidos. A opção de manutenção é uma declaração do operador; a ferramenta não encerra serviços automaticamente.

A ferramenta valida as confirmações, grava o manifesto, habilita a autorização apenas na conexão da migração e aplica a alteração transacional. Uma migração comum sobre dados operacionais existentes é bloqueada. Após o commit, apaga apenas os arquivos referenciados e os artefatos conhecidos de cada trabalho, previamente conferidos como filhos diretos do diretório; links e caminhos externos são recusados. Os demais arquivos permanecem. Por fim, compara o conteúdo dos cadastros preservados; auditoria e permissões são aditivas e têm verificação específica no ensaio de integração.

## Conferência antes de reabrir

- Conferir tabelas operacionais vazias e famílias/vínculos inicialmente vazios, conteúdo preservado e evento de reinicialização.
- Conferir arquivos removidos contra o manifesto e ausência de importações antigas aguardando aprovação.
- Iniciar a versão nova de API e worker; validar `/health/live` e `/health/ready`, login, permissões e um fluxo autorizado de família → imóvel → visita.
- Publicar os frontends compilados do mesmo checkout e conferir a atualização da PWA; nenhum dado familiar ou visita deve entrar no cache de runtime.
- Reexecutar a pré-implantação e obter nova decisão de liberação do piloto. Aprovações antigas permanecem como histórico, mas não liberam o estado novo.

Se a migração falhar, a transação não é confirmada e os arquivos não são apagados. Se a limpeza de arquivos ou a conferência falhar **depois** do commit, manter a manutenção: o banco já estará no modelo novo. Usar o manifesto para conferir a limpeza restante; a ferramenta recusa repetir o reset após a migração. Não tentar reverter com `Down`, que é bloqueado; restaurar o backup completo se for necessário voltar ao modelo anterior.
