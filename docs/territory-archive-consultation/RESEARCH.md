# Consulta de microrregiões arquivadas

## Problema

Ao arquivar uma microrregião, ela deixa corretamente o mapa operacional, mas também desaparece da consulta da UBS. Isso impede consultar seu histórico e fazer correções posteriores.

## Decisões confirmadas

- A consulta será exclusiva para microrregiões; bairros arquivados não fazem parte desta entrega.
- A microrregião poderá ser editada sem ser reativada.
- A edição manterá o registro arquivado e criará uma nova versão e auditoria.
- Contornos arquivados não serão misturados ao mapa operacional.
- Somente o contorno arquivado selecionado será mostrado no mapa.
- A consulta continuará limitada à UBS e à permissão de gestão territorial.
- Não haverá exclusão definitiva.
- Nome e código serão exclusivos somente entre microrregiões ativas da mesma UBS.
- Uma área arquivada não bloqueará o reaproveitamento de seus identificadores.
- A reativação será bloqueada quando houver conflito de nome, código, bairros ou sobreposição territorial; o registro arquivado poderá ser corrigido antes de nova tentativa.
- Gerentes poderão reativar áreas da própria UBS e o administrador global poderá reativar em qualquer UBS autorizada.

## Abordagem

Usar uma consulta separada na API para registros arquivados. A interface mantém duas coleções: a camada operacional ativa e a lista arquivada. Selecionar um card arquivado envia apenas sua geometria para a camada temporária de destaque já existente.

## Riscos tratados

- Exposição entre UBS: escopo validado no servidor.
- Poluição visual: arquivadas nunca entram na camada territorial principal.
- Perda de estado: atualização usa concorrência otimista, versão e auditoria existentes.
- Reativação inconsistente: todas as condições operacionais são revalidadas antes de restaurar.
- Corrida entre cadastros: índices parciais no banco garantem a unicidade dos registros ativos.
