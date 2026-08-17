# ADR-016 — Retenção de sessão e staging

Estado: Aceita

## Contexto

Sessões e evidências podem conter informações privadas mesmo após uma falha.

## Decisão

Checkpoints não sensíveis vivem em `chrome.storage.session`. A extensão limpa a
sessão após download confirmado ou exclusão explícita. Stagings do editor têm
ID aleatório, expiração, acesso por token local e limpeza idempotente.

## Alternativas recusadas

- Retenção indefinida: aumenta exposição.
- Limpeza antes do download: pode causar perda de trabalho.

## Consequências

Falhas deixam estado recuperável apenas dentro da janela configurada. Evidência
removida não invalida o fluxo.

## Rollback

Limpeza manual segura das áreas de sessão/staging.

## Testes e evidências

Suspensão simulada, expiração, cancelamento e limpeza repetida.
