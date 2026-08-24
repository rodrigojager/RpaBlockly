# ADR-014 — Permissões mínimas

Estado: Aceita

## Contexto

Uma extensão de gravação observa páginas potencialmente sensíveis. Permissão
permanente e ampla seria incompatível com consentimento informado.

## Decisão

Declarar apenas `activeTab`, `scripting`, `storage`, `downloads` e `sidePanel`.
Não usar `debugger`, `webRequest`, `nativeMessaging` nem hosts permanentes.
Declarar `<all_urls>` somente em `optional_host_permissions`: o painel o solicita
por gesto apenas quando screenshots estão ligados, pois `captureVisibleTab` não
aceita os padrões separados de HTTP e HTTPS como substituto. Sem screenshots, a
sessão solicita somente HTTP(S). Toda concessão criada pela sessão é retirada ao
concluir, excluir ou falhar, e o Recorder rejeita alvos fora de HTTP(S).

## Alternativas recusadas

- Acesso permanente a todos os sites: privilégio excessivo.
- Depender apenas de `activeTab`: deixa de funcionar depois da navegação da aba.
- Native messaging: cria companion app fora do escopo.

## Consequências

Frames cross-origin sem permissão geram issue explícita. O usuário controla o
alcance de cada sessão.

## Rollback

Revogar a permissão de host ou desinstalar a extensão.

## Testes e evidências

Lint do manifest, testes de solicitação por gesto e fixture cross-origin.
