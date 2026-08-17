# ADR-014 — Permissões mínimas

Estado: Aceita

## Contexto

Uma extensão de gravação observa páginas potencialmente sensíveis. Permissão
permanente e ampla seria incompatível com consentimento informado.

## Decisão

Declarar apenas `activeTab`, `scripting`, `storage`, `downloads` e `sidePanel`.
Hosts são opcionais e solicitados por gesto para a origem ativa. Não usar
`debugger`, `webRequest`, `nativeMessaging` ou `<all_urls>` permanente.

## Alternativas recusadas

- Acesso permanente a todos os sites: privilégio excessivo.
- Native messaging: cria companion app fora do escopo.

## Consequências

Frames cross-origin sem permissão geram issue explícita. O usuário controla o
alcance de cada sessão.

## Rollback

Revogar a permissão de host ou desinstalar a extensão.

## Testes e evidências

Lint do manifest, testes de solicitação por gesto e fixture cross-origin.
