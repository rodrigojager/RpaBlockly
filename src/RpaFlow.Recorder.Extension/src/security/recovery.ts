import { fromBase64, toBase64 } from "./secrets.js";

const recoveryPrefix = "rpablockly-recorder-key-v1.";
const recoveryType = "rpablockly-recorder-recovery-key";
const recoveryAadPrefix = "RpaBlockly Recorder recovery key v1";
const pbkdf2Iterations = 600_000;
const rsaModulusLength = 3_072;
const passwordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
const passwordLetters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
const passwordDigits = "23456789";

interface RecoveryKeyPackageV1 {
  schemaVersion: 1;
  type: typeof recoveryType;
  keyId: string;
  keyAlgorithm: "RSA-OAEP-SHA-256";
  modulusLength: typeof rsaModulusLength;
  protection: {
    algorithm: "PBKDF2-HMAC-SHA-256+AES-256-GCM";
    iterations: typeof pbkdf2Iterations;
    salt: string;
    iv: string;
    ciphertext: string;
  };
}

export interface GeneratedRecipientAccess {
  keyId: string;
  publicKeyPem: string;
  recoveryKey: string;
}

export function validateSharingPassword(password: string): void {
  if (password.length < 12 || password.length > 128 || password.trim() !== password) {
    throw new Error("A senha deve ter de 12 a 128 caracteres e não pode começar ou terminar com espaço.");
  }
  if (!/\p{L}/u.test(password) || !/\p{N}/u.test(password)) {
    throw new Error("A senha deve conter ao menos uma letra e um número.");
  }
}

export function generateSharingPassword(length = 24): string {
  if (!Number.isInteger(length) || length < 12 || length > 128) {
    throw new Error("O tamanho da senha gerada é inválido.");
  }
  const characters = [
    randomCharacter(passwordLetters),
    randomCharacter(passwordDigits)
  ];
  while (characters.length < length) characters.push(randomCharacter(passwordAlphabet));
  for (let index = characters.length - 1; index > 0; index -= 1) {
    const other = randomIndex(index + 1);
    [characters[index], characters[other]] = [characters[other]!, characters[index]!];
  }
  return characters.join("");
}

export async function generateRecipientAccess(password: string): Promise<GeneratedRecipientAccess> {
  validateSharingPassword(password);
  const pair = await crypto.subtle.generateKey({
    name: "RSA-OAEP",
    modulusLength: rsaModulusLength,
    publicExponent: new Uint8Array([1, 0, 1]),
    hash: "SHA-256"
  }, true, ["encrypt", "decrypt"]) as CryptoKeyPair;
  const publicBytes = new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey));
  const fingerprint = new Uint8Array(await crypto.subtle.digest("SHA-256", publicBytes));
  const keyId = `generated-${toBase64Url(fingerprint).slice(0, 24)}`;
  const privateBytes = new Uint8Array(await crypto.subtle.exportKey("pkcs8", pair.privateKey));
  const passwordBytes = new TextEncoder().encode(password);
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const aad = new TextEncoder().encode(`${recoveryAadPrefix}:${keyId}`);
  try {
    const protectionKey = await deriveProtectionKey(passwordBytes, salt, ["encrypt"]);
    const ciphertext = new Uint8Array(await crypto.subtle.encrypt({
      name: "AES-GCM",
      iv: iv as BufferSource,
      additionalData: aad as BufferSource,
      tagLength: 128
    }, protectionKey, privateBytes as BufferSource));
    const payload: RecoveryKeyPackageV1 = {
      schemaVersion: 1,
      type: recoveryType,
      keyId,
      keyAlgorithm: "RSA-OAEP-SHA-256",
      modulusLength: rsaModulusLength,
      protection: {
        algorithm: "PBKDF2-HMAC-SHA-256+AES-256-GCM",
        iterations: pbkdf2Iterations,
        salt: toBase64(salt),
        iv: toBase64(iv),
        ciphertext: toBase64(ciphertext)
      }
    };
    return {
      keyId,
      publicKeyPem: formatPem("PUBLIC KEY", publicBytes),
      recoveryKey: recoveryPrefix + toBase64Url(new TextEncoder().encode(JSON.stringify(payload)))
    };
  } finally {
    privateBytes.fill(0);
    passwordBytes.fill(0);
    publicBytes.fill(0);
    fingerprint.fill(0);
    salt.fill(0);
    iv.fill(0);
    aad.fill(0);
  }
}

export async function decryptRecoveryKey(password: string, recoveryKey: string): Promise<{
  keyId: string;
  privateKeyPkcs8: Uint8Array;
}> {
  validateSharingPassword(password);
  const payload = parseRecoveryKey(recoveryKey);
  const passwordBytes = new TextEncoder().encode(password);
  const salt = fromBase64(payload.protection.salt);
  const iv = fromBase64(payload.protection.iv);
  const ciphertext = fromBase64(payload.protection.ciphertext);
  const aad = new TextEncoder().encode(`${recoveryAadPrefix}:${payload.keyId}`);
  try {
    const protectionKey = await deriveProtectionKey(passwordBytes, salt, ["decrypt"]);
    const privateKeyPkcs8 = new Uint8Array(await crypto.subtle.decrypt({
      name: "AES-GCM",
      iv: iv as BufferSource,
      additionalData: aad as BufferSource,
      tagLength: 128
    }, protectionKey, ciphertext as BufferSource));
    return { keyId: payload.keyId, privateKeyPkcs8 };
  } catch {
    throw new Error("A senha ou a chave de recuperação está incorreta.");
  } finally {
    passwordBytes.fill(0);
    salt.fill(0);
    iv.fill(0);
    ciphertext.fill(0);
    aad.fill(0);
  }
}

export function formatPem(label: "PUBLIC KEY" | "PRIVATE KEY", bytes: Uint8Array): string {
  const base64 = toBase64(bytes);
  const lines = base64.match(/.{1,64}/gu) ?? [];
  return `-----BEGIN ${label}-----\n${lines.join("\n")}\n-----END ${label}-----`;
}

function parseRecoveryKey(recoveryKey: string): RecoveryKeyPackageV1 {
  const trimmed = recoveryKey.trim();
  if (!trimmed.startsWith(recoveryPrefix) || trimmed.length > 10_000) {
    throw new Error("A chave de recuperação não pertence ao RpaBlockly Recorder.");
  }
  let value: unknown;
  try {
    value = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(
      fromBase64Url(trimmed.slice(recoveryPrefix.length))
    ));
  } catch {
    throw new Error("A chave de recuperação está corrompida.");
  }
  if (value === null || typeof value !== "object") throw new Error("A chave de recuperação é inválida.");
  const payload = value as Partial<RecoveryKeyPackageV1>;
  const protection = payload.protection;
  if (payload.schemaVersion !== 1 || payload.type !== recoveryType ||
      typeof payload.keyId !== "string" || !/^generated-[A-Za-z0-9_-]{24}$/u.test(payload.keyId) ||
      payload.keyAlgorithm !== "RSA-OAEP-SHA-256" || payload.modulusLength !== rsaModulusLength ||
      protection?.algorithm !== "PBKDF2-HMAC-SHA-256+AES-256-GCM" ||
      protection.iterations !== pbkdf2Iterations || !isBase64(protection.salt, 16) ||
      !isBase64(protection.iv, 12) || !isBase64(protection.ciphertext, undefined, 512, 8_192)) {
    throw new Error("A chave de recuperação usa um contrato inválido.");
  }
  return payload as RecoveryKeyPackageV1;
}

async function deriveProtectionKey(
  passwordBytes: Uint8Array,
  salt: Uint8Array,
  usages: KeyUsage[]
): Promise<CryptoKey> {
  const material = await crypto.subtle.importKey(
    "raw",
    passwordBytes as BufferSource,
    "PBKDF2",
    false,
    ["deriveKey"]
  );
  return await crypto.subtle.deriveKey({
    name: "PBKDF2",
    hash: "SHA-256",
    salt: salt as BufferSource,
    iterations: pbkdf2Iterations
  }, material, { name: "AES-GCM", length: 256 }, false, usages);
}

function isBase64(value: unknown, exactBytes?: number, minimumBytes?: number, maximumBytes?: number): value is string {
  if (typeof value !== "string" || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value)) {
    return false;
  }
  const size = fromBase64(value).length;
  return (exactBytes === undefined || size === exactBytes) &&
    (minimumBytes === undefined || size >= minimumBytes) &&
    (maximumBytes === undefined || size <= maximumBytes);
}

function toBase64Url(bytes: Uint8Array): string {
  return toBase64(bytes).replace(/\+/gu, "-").replace(/\//gu, "_").replace(/=+$/gu, "");
}

function fromBase64Url(value: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/u.test(value)) throw new Error("Base64url inválido.");
  const base64 = value.replace(/-/gu, "+").replace(/_/gu, "/");
  return fromBase64(base64.padEnd(Math.ceil(base64.length / 4) * 4, "="));
}

function randomCharacter(alphabet: string): string {
  return alphabet[randomIndex(alphabet.length)]!;
}

function randomIndex(maximum: number): number {
  const limit = 256 - (256 % maximum);
  const sample = new Uint8Array(1);
  do crypto.getRandomValues(sample); while (sample[0]! >= limit);
  return sample[0]! % maximum;
}
