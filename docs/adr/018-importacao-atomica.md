# ADR-018 — Importação em duas fases

Estado: Aceita

## Contexto

Preview e cancelamento não podem alterar o pacote aberto. O apply precisa
respeitar concorrência e resolver conflitos explicitamente.

## Decisão

`inspect` e `validate` atuam somente em staging. O wizard define modo, mappings
e resoluções; então o backend monta o resultado em memória, valida e publica
atomicamente com a revisão esperada pelo package store.

## Alternativas recusadas

- Extrair no projeto durante upload: deixa estado parcial.
- Sobrescrever em conflito: perde trabalho concorrente.
- Merge silencioso por nome: pode mudar semântica.

## Consequências

Há três modos explícitos: substituir, acrescentar ao principal ou subflow.
Cancelar e repetir são idempotentes.

## Rollback

Selecionar uma revisão anterior no histórico; staging pode ser descartado.

## Testes e evidências

Read-only, CAS, três modos, remapeamento determinístico e reabertura sem perda.
