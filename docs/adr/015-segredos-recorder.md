# ADR-015 — Segredos do Recorder

Estado: Aceita

## Contexto

Senha em texto claro no storage, screenshot ou ZIP seria um vazamento grave.

## Decisão

Captura inicia desligada. No modo simples, o usuário informa uma senha textual e
a extensão gera localmente um par RSA-OAEP de 3072 bits. A chave privada existe
somente durante essa geração, é exportada para memória, cifrada com
AES-256-GCM e descartada; a chave de proteção é derivada da senha por
PBKDF2-HMAC-SHA-256 com salt aleatório e 600.000 iterações. O usuário recebe uma
chave de recuperação cifrada e precisa confirmar que copiou os dois itens antes
de iniciar.

O modo avançado continua aceitando o key ID e a chave pública RSA/SPKI fornecidos
pelo desenvolvedor. Em ambos os modos, cada segredo usa AES-256-GCM com nonce
único e AAD, e sua chave AES é envolvida por RSA-OAEP-SHA-256. O fluxo guarda
somente `secret.recorded.*`; senha de compartilhamento, chave de recuperação e
chave privada não entram no checkpoint nem no bundle.

## Alternativas recusadas

- Texto claro: inaceitável.
- Ofuscação ou criptografia própria: não oferece garantia adequada.
- Senha como única chave direta: facilita ataques de tentativa e não preserva o
  envelope RSA já auditado.
- Persistir a senha ou a chave privada no frontend: amplia a exposição e permite
  recuperação sem o consentimento do usuário.

## Consequências

Sem material válido não há captura. A pessoa deve repassar a senha e a chave de
recuperação, preferencialmente por canais separados; se perder qualquer uma, a
chave privada não poderá ser recuperada. O backend pode remapear um segredo sem
devolvê-lo ao JavaScript. O modo avançado não muda e continua adequado a
ambientes com gestão externa de chaves.

## Rollback

Descartar `secrets/` e manter as referências pendentes no editor.

## Testes e evidências

Round-trip criptográfico do modo simples até o segredo, senha errada, utilitário
de recuperação, chave RSA errada, nonce, AAD e busca de texto claro.
