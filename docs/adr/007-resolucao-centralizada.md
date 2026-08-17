# ADR-007 — Resolução centralizada

## Estado

Aceita.

## Contexto

O interpretador histórico resolvia seletores em vários handlers. Fallback,
orçamento, diagnóstico e heurística seriam inconsistentes se cada bloco mantivesse
sua própria chamada à API do navegador.

## Decisão

Todo elemento de negócio é obtido por `LocatorResolver`. A receita é compilada na
ordem frames externos → internos, scope, filtro de scope, target e filtro de target.
Handlers recebem um resultado já validado quanto a cardinalidade e estado.

## Alternativas recusadas

- helper por handler: ainda permite desvios e políticas diferentes;
- selector string pré-compilado: não representa role, frames e texto dinâmico;
- fallback no bloco: duplica catálogo e diagnóstico.

## Consequências

Target, condition, trigger, options, ready, success, protocol e download seguem o
mesmo orçamento e classificação de falhas. Acesso direto a locators fica restrito
à infraestrutura autorizada.

## Rollback

Configurar política `strict` reduz o resolver ao primeiro candidato sem reintroduzir
acesso direto. Rollback de código deve manter a interface para preservar handlers.

## Testes e evidências

`RpaFlow.PlaywrightChecks` cobre as nove estratégias, todos os papéis auxiliares,
frames, scope, cardinalidade, orçamento e diferencial estrito V1/V2. Um check
arquitetural procura acessos diretos fora da allowlist.
