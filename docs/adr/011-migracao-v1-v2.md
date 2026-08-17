# ADR-011 — Migração V1 → V2

## Estado

Aceita.

## Contexto

O schema 1 contém seletores em vários papéis e usa `target` também como destino de
dados. Inferir equivalências ou deduplicar seletores iguais poderia mudar
cardinalidade, intenção e ordem sem revisão humana.

## Decisão

A migração é offline, determinística e não sobrescreve a origem. IDs seguem
`{actionId}.target`, `.trigger`, `.options`, `.ready`, `.success`, `.protocol` e
`.condition`. Seletores iguais não são deduplicados. `rawPlaywright` preserva a
expressão aceita anteriormente. O antigo `target` de saída vira `output`.

## Alternativas recusadas

- conversão no worker: mistura migração e execução;
- deduplicação por string: seletores iguais podem ter papéis distintos;
- reinterpretar CSS/XPath automaticamente: arrisca mudar semântica;
- sobrescrever a origem: elimina comparação e rollback.

## Consequências

O migrador produz pacote, política strict, relatório e warnings de revisão humana.
IDs são previsíveis e cada campo antigo possui destino mecânico documentado.

## Rollback

Descartar o diretório de saída e continuar usando a origem com o release V1. O
migrador oferece `--dry-run`, saída separada e backup quando solicitado.

## Testes e evidências

`RpaFlow.MigratorChecks` valida fixtures por família e agregada, determinismo,
papéis auxiliares, ausência de deduplicação e pacote resultante. O teste diferencial
Playwright compara ações e saídas observáveis de V1 e V2.
