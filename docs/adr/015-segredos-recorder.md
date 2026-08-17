# ADR-015 — Segredos do Recorder

Estado: Aceita

## Contexto

Senha em texto claro no storage, screenshot ou ZIP seria um vazamento grave.

## Decisão

Captura inicia desligada. Consentimento por sessão exige chave RSA pública e
key ID. Cada segredo usa AES-256-GCM com nonce único e AAD; a chave AES é
envolvida por RSA-OAEP-SHA-256. O fluxo guarda somente `secret.recorded.*`.

## Alternativas recusadas

- Texto claro: inaceitável.
- Ofuscação ou criptografia própria: não oferece garantia adequada.
- Chave privada no frontend: expõe o material de descriptografia.

## Consequências

Sem chave válida não há captura. O backend pode remapear um segredo sem
devolvê-lo ao JavaScript.

## Rollback

Descartar `secrets/` e manter as referências pendentes no editor.

## Testes e evidências

Round-trip criptográfico, chave errada, nonce, AAD e busca de texto claro.
