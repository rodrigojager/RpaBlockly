# ADR-004 — JSON determinístico

## Estado

Aceita.

## Contexto

Revisão, compare-and-swap e integridade dependem de hashes estáveis. Serializadores
podem variar espaços, fim de linha e ordem de propriedades sem mudar a semântica,
enquanto a ordem de arrays de ações e candidatos é parte do comportamento.

## Decisão

A forma canônica usa UTF-8 sem BOM, chaves de objeto em ordem ordinal, arrays na
ordem de negócio e representação JSON nativa. Arquivos legíveis usam indentação e
LF; hashes usam a forma canônica sem espaços.

## Alternativas recusadas

- hash dos bytes gravados: muda com formatação;
- ordenar arrays: alteraria roteiro e prioridade dos candidatos;
- hash apenas do fluxo: não identifica locator ou política utilizados.

## Consequências

Ordem de propriedades não altera o hash; trocar candidatos, ações ou documentos
altera. Ferramentas devem preservar Unicode e não normalizar arrays.

## Rollback

Revisões antigas permanecem endereçáveis pelo hash já publicado. Uma futura regra
canônica precisa de versão própria e migração explícita, nunca recálculo silencioso.

## Testes e evidências

`RpaFlow.PackagesChecks` compara propriedades reordenadas, conteúdo modificado e
ordem de candidatos. `RpaFlow.MigratorChecks` prova saída determinística para a
mesma entrada.
