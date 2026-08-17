# ADR-006 — Snapshot de execução

## Estado

Aceita.

## Contexto

Pacotes podem ser publicados enquanto execuções estão em andamento. Recarregar
por ação criaria uma execução híbrida e sincronizaria casos independentes.

## Decisão

O worker resolve, carrega e valida um `RpaPackageSnapshot` antes da primeira ação.
O executor mantém essa instância imutável até o resultado final e não realiza I/O
de pacote durante ações. Revisão e hash entram no registro da execução.

## Alternativas recusadas

- cache TTL: tempo não identifica conteúdo nem consistência;
- reload por etapa: mistura revisões;
- lock durante toda a execução: bloqueia publicação e outros workers.

## Consequências

Publicação e aprendizado não alteram casos em andamento. Execuções do mesmo RPA
podem usar revisões diferentes sem lockstep.

## Rollback

Novas execuções podem apontar para uma revisão anterior; execuções já iniciadas
terminam no snapshot que carregaram ou são canceladas pelo mecanismo normal.

## Testes e evidências

Checks de packages, SQL e worker publicam nova revisão durante leituras fixadas e
confirmam revisão/hash independentes por execução.
