import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  decryptRecoveryKey,
  generateRecipientAccess,
  generateSharingPassword,
  validateSharingPassword
} from "../src/security/recovery.js";
import { encryptSecret, fromBase64, toBase64 } from "../src/security/secrets.js";

const extensionRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

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

test("modo simples gera senha e chave recuperável somente com a senha correta", async () => {
  const generatedPassword = generateSharingPassword();
  assert.equal(generatedPassword.length, 24);
  assert.match(generatedPassword, /[A-Za-z]/u);
  assert.match(generatedPassword, /[0-9]/u);
  validateSharingPassword(generatedPassword);

  const password = "RecorderSeguro2026";
  const access = await generateRecipientAccess(password);
  assert.match(access.keyId, /^generated-[A-Za-z0-9_-]{24}$/u);
  assert.match(access.publicKeyPem, /BEGIN PUBLIC KEY/u);
  assert.match(access.recoveryKey, /^rpablockly-recorder-key-v1\./u);
  assert.doesNotMatch(access.recoveryKey, new RegExp(password, "u"));

  await assert.rejects(
    () => decryptRecoveryKey("SenhaIncorreta2026", access.recoveryKey),
    /senha ou a chave de recuperação está incorreta/iu
  );

  const recovered = await decryptRecoveryKey(password, access.recoveryKey);
  assert.equal(recovered.keyId, access.keyId);
  const privateKey = await crypto.subtle.importKey(
    "pkcs8",
    recovered.privateKeyPkcs8 as BufferSource,
    { name: "RSA-OAEP", hash: "SHA-256" },
    false,
    ["decrypt"]
  );
  const envelope = await encryptSecret(
    "secret.recorded.simple",
    new TextEncoder().encode("valor-protegido"),
    { keyId: access.keyId, pem: access.publicKeyPem },
    "bundle-simple"
  );
  const rawAes = await crypto.subtle.decrypt(
    { name: "RSA-OAEP" }, privateKey, fromBase64(envelope.wrappedKey) as BufferSource
  );
  const aes = await crypto.subtle.importKey("raw", rawAes, { name: "AES-GCM" }, false, ["decrypt"]);
  const plaintext = await crypto.subtle.decrypt({
    name: "AES-GCM",
    iv: fromBase64(envelope.iv) as BufferSource,
    additionalData: fromBase64(envelope.aad) as BufferSource
  }, aes, fromBase64(envelope.ciphertext) as BufferSource);
  assert.equal(new TextDecoder().decode(plaintext), "valor-protegido");

  const directory = await mkdtemp(join(tmpdir(), "rpablockly-recovery-"));
  try {
    const packagePath = join(directory, "chave.txt");
    const outputPath = join(directory, "private.pem");
    await writeFile(packagePath, access.recoveryKey, "utf8");
    const result = spawnSync(process.execPath, [
      join(extensionRoot, "scripts", "recover-key.mjs"),
      "--package", packagePath,
      "--output", outputPath
    ], { input: `${password}\n`, encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
    const pem = await readFile(outputPath, "utf8");
    assert.match(pem, /BEGIN PRIVATE KEY/u);
    const recoveredFromCli = fromBase64(pem.replace(/-----(?:BEGIN|END) PRIVATE KEY-----|\s/gu, ""));
    assert.deepEqual(recoveredFromCli, recovered.privateKeyPkcs8);
    recoveredFromCli.fill(0);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
  recovered.privateKeyPkcs8.fill(0);
});

test("modo simples rejeita senha curta ou sem combinação de letras e números", () => {
  assert.throws(() => validateSharingPassword("curta1"), /12 a 128/u);
  assert.throws(() => validateSharingPassword("somenteletras"), /letra e um número/u);
  assert.throws(() => validateSharingPassword("123456789012"), /letra e um número/u);
});

async function keyPair(): Promise<CryptoKeyPair> {
  return await crypto.subtle.generateKey({
    name: "RSA-OAEP", modulusLength: 2_048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256"
  }, true, ["encrypt", "decrypt"]) as CryptoKeyPair;
}
