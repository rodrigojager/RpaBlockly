# ADR-009 — Aprendizado confirmado

## Estado

Aceita.

## Contexto

Um alvo heurístico pode permitir uma ação intermediária e ainda assim levar a uma
falha posterior. Persisti-lo imediatamente contaminaria execuções futuras com uma
hipótese não confirmada.

## Decisão

Aprendizado é provisório e isolado por `executionId`. A própria execução pode
reutilizá-lo. Somente resultado final `Succeeded` confirma promoções. `Validated`,
`Failed`, `Retry`, `Cancelled` e encerramento inesperado descartam tudo.

## Alternativas recusadas

- persistir na primeira resolução: confirma antes do resultado de negócio;
- compartilhar cache provisório: vaza decisão entre execuções;
- exigir várias execuções para promover: impede promoção imediata já aprovada pelo
  contrato de sucesso completo.

## Consequências

Há diagnóstico de promoção, descarte e conflito por observação. O snapshot original
nunca é mutado e nenhuma execução aguarda outra terminar.

## Rollback

Definir `promotion: disabled` ou `learningWriteBack: disabled`; candidatos já
publicados podem ser revertidos por nova revisão via CAS.

## Testes e evidências

`RpaFlow.PlaywrightChecks` cobre reuso dentro da execução, isolamento paralelo,
todos os estados finais e promoção somente após sucesso.
