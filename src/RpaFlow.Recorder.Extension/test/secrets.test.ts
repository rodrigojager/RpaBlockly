import assert from "node:assert/strict";
import test from "node:test";
import { encryptSecret, fromBase64, toBase64 } from "../src/security/secrets.js";

test("segredo usa AES-256-GCM e RSA-OAEP-SHA-256 sem texto claro", async () => {
  const pair = await crypto.subtle.generateKey({
    name: "RSA-OAEP", modulusLength: 2_048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256"
  }, true, ["encrypt", "decrypt"]);
  const spki = new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey));
  const pem = `-----BEGIN PUBLIC KEY-----\n${toBase64(spki)}\n-----END PUBLIC KEY-----`;
  const original = "senha-ultrassecreta";
  const plaintext = new TextEncoder().encode(original);
  const envelope = await encryptSecret(
    "secret.recorded.password",
    plaintext,
    { keyId: "fixture-key", pem },
    "bundle-fixture"
  );
  assert.equal(plaintext.every((byte) => byte === 0), true);
  assert.doesNotMatch(JSON.stringify(envelope), new RegExp(original, "u"));
  const rawAes = await crypto.subtle.decrypt(
    { name: "RSA-OAEP" }, pair.privateKey, fromBase64(envelope.wrappedKey) as BufferSource
  );
  const aes = await crypto.subtle.importKey("raw", rawAes, { name: "AES-GCM" }, false, ["decrypt"]);
  const decrypted = await crypto.subtle.decrypt({
    name: "AES-GCM",
    iv: fromBase64(envelope.iv) as BufferSource,
    additionalData: fromBase64(envelope.aad) as BufferSource
  }, aes, fromBase64(envelope.ciphertext) as BufferSource);
  assert.equal(new TextDecoder().decode(decrypted), original);
});

test("chave destinatária errada não abre a chave simétrica", async () => {
  const recipient = await keyPair();
  const wrong = await keyPair();
  const spki = new Uint8Array(await crypto.subtle.exportKey("spki", recipient.publicKey));
  const envelope = await encryptSecret(
    "secret.recorded.password",
    new TextEncoder().encode("segredo"),
    { keyId: "recipient", pem: `-----BEGIN PUBLIC KEY-----\n${toBase64(spki)}\n-----END PUBLIC KEY-----` },
    "bundle-fixture"
  );
  await assert.rejects(() => crypto.subtle.decrypt(
    { name: "RSA-OAEP" }, wrong.privateKey, fromBase64(envelope.wrappedKey) as BufferSource
  ));
});

async function keyPair(): Promise<CryptoKeyPair> {
  return await crypto.subtle.generateKey({
    name: "RSA-OAEP", modulusLength: 2_048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256"
  }, true, ["encrypt", "decrypt"]) as CryptoKeyPair;
}
