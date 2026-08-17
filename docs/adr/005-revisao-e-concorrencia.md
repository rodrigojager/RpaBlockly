# ADR-005 — Revisão e concorrência

## Estado

Aceita.

## Contexto

Editor, aprendizado e publicação podem disputar o mesmo pacote. Um simples
“último escritor vence” perderia alterações e poderia misturar decisões feitas a
partir de bases diferentes.

## Decisão

A revisão é opaca ao consumidor e deriva do SHA-256 do conteúdo canônico dos três
documentos. Publicação usa compare-and-swap contra a revisão esperada; revisão
ausente só cria pacote ainda inexistente. Revisões publicadas são imutáveis.

## Alternativas recusadas

- contador global: exige coordenação central e não prova conteúdo;
- timestamp: pode colidir e depende de relógio;
- sobrescrita incondicional: perde atualização concorrente.

## Consequências

Há um vencedor determinístico por revisão esperada. Conflitos chegam ao editor ou
ao write-back para recarregar, comparar ou reaplicar; nunca são ocultados.

## Rollback

Publicar novamente o conteúdo de uma revisão anterior usando CAS sobre a revisão
atual, preservando o histórico.

## Testes e evidências

`RpaFlow.PackagesChecks` e `RpaFlow.Packages.SqlServerChecks` exercitam escritores
concorrentes, histórico e leitura fixada. Falhas injetadas em cada etapa do store
de arquivo preservam a revisão anterior.
