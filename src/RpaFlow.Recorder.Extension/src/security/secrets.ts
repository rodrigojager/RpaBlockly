import type { EncryptedSecretEnvelope } from "../core/types.js";

export interface RecipientPublicKey {
  keyId: string;
  pem: string;
}

export async function validateRecipientKey(recipient: RecipientPublicKey): Promise<CryptoKey> {
  if (recipient.keyId.trim().length === 0 || recipient.keyId.length > 200) {
    throw new Error("O key ID do destinatário é inválido.");
  }
  const key = await crypto.subtle.importKey(
    "spki",
    pemToBytes(recipient.pem) as BufferSource,
    { name: "RSA-OAEP", hash: "SHA-256" },
    false,
    ["encrypt"]
  );
  const algorithm = key.algorithm as RsaHashedKeyAlgorithm;
  if (algorithm.modulusLength < 2_048) throw new Error("A chave RSA deve ter ao menos 2048 bits.");
  return key;
}

export async function encryptSecret(
  reference: string,
  plaintext: Uint8Array,
  recipient: RecipientPublicKey,
  bundleId: string
): Promise<EncryptedSecretEnvelope> {
  if (!/^secret\.recorded\.[A-Za-z][A-Za-z0-9_-]*$/u.test(reference)) {
    throw new Error("Referência de segredo inválida.");
  }
  const publicKey = await validateRecipientKey(recipient);
  const aesKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, true, ["encrypt"]);
  const rawKey = new Uint8Array(await crypto.subtle.exportKey("raw", aesKey));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const aadText = `${bundleId}:${reference}:${recipient.keyId}`;
  const aad = new TextEncoder().encode(aadText);
  try {
    const [ciphertext, wrappedKey] = await Promise.all([
      crypto.subtle.encrypt(
        { name: "AES-GCM", iv: iv as BufferSource, additionalData: aad as BufferSource },
        aesKey,
        plaintext as BufferSource
      ),
      crypto.subtle.encrypt({ name: "RSA-OAEP" }, publicKey, rawKey as BufferSource)
    ]);
    return {
      schemaVersion: 1,
      reference,
      keyId: recipient.keyId,
      algorithm: "AES-256-GCM+RSA-OAEP-SHA-256",
      iv: toBase64(iv),
      aad: toBase64(aad),
      ciphertext: toBase64(new Uint8Array(ciphertext)),
      wrappedKey: toBase64(new Uint8Array(wrappedKey))
    };
  } finally {
    plaintext.fill(0);
    rawKey.fill(0);
    aad.fill(0);
    iv.fill(0);
  }
}

export function pemToBytes(pem: string): Uint8Array {
  const match = pem.match(/-----BEGIN PUBLIC KEY-----([\s\S]+?)-----END PUBLIC KEY-----/u);
  if (match?.[1] === undefined) throw new Error("Chave pública deve estar no formato PEM/SPKI.");
  return fromBase64(match[1].replace(/\s+/gu, ""));
}

export function toBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let index = 0; index < bytes.length; index += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(index, index + 0x8000));
  }
  return btoa(binary);
}

export function fromBase64(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}
