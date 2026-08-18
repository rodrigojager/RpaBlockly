import { webcrypto } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";

const recoveryPrefix = "rpablockly-recorder-key-v1.";
const recoveryType = "rpablockly-recorder-recovery-key";
const recoveryAadPrefix = "RpaBlockly Recorder recovery key v1";
const pbkdf2Iterations = 600_000;
const rsaModulusLength = 3_072;

try {
  const options = parseArguments(process.argv.slice(2));
  if (options.help) {
    showHelp();
  } else {
    const password = await readHiddenPassword();
    const recoveryKey = await readFile(options.packagePath, "utf8");
    const recovered = await decryptRecoveryKey(password, recoveryKey);
    const privateKey = formatPem("PRIVATE KEY", recovered.privateKeyPkcs8);
    try {
      await writeFile(options.outputPath, `${privateKey}\n`, { encoding: "utf8", flag: "wx", mode: 0o600 });
    } finally {
      recovered.privateKeyPkcs8.fill(0);
    }
    process.stdout.write(`Chave ${recovered.keyId} recuperada em ${options.outputPath}. Proteja e exclua o arquivo quando não for mais necessário.\n`);
  }
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : "Falha ao recuperar a chave."}\n`);
  process.exitCode = 1;
}

function parseArguments(args) {
  if (args.includes("--help") || args.includes("-h")) return { help: true };
  const packageIndex = args.indexOf("--package");
  const outputIndex = args.indexOf("--output");
  const packagePath = packageIndex < 0 ? undefined : args[packageIndex + 1];
  const outputPath = outputIndex < 0 ? undefined : args[outputIndex + 1];
  if (!packagePath || !outputPath) {
    throw new Error("Uso: npm run recover:key -- --package <chave.txt> --output <chave-privada.pem>");
  }
  return { help: false, packagePath, outputPath };
}

function showHelp() {
  process.stdout.write([
    "Recupera a chave privada do Recorder sem enviar dados para a rede.",
    "",
    "Uso:",
    "  npm run recover:key -- --package <chave.txt> --output <chave-privada.pem>",
    "",
    "O comando solicita a senha sem exibi-la e não sobrescreve o arquivo de saída.",
    ""
  ].join("\n"));
}

async function decryptRecoveryKey(password, recoveryKey) {
  validatePassword(password);
  const payload = parseRecoveryKey(recoveryKey);
  const passwordBytes = new TextEncoder().encode(password);
  const salt = Buffer.from(payload.protection.salt, "base64");
  const iv = Buffer.from(payload.protection.iv, "base64");
  const ciphertext = Buffer.from(payload.protection.ciphertext, "base64");
  const aad = new TextEncoder().encode(`${recoveryAadPrefix}:${payload.keyId}`);
  try {
    const material = await webcrypto.subtle.importKey("raw", passwordBytes, "PBKDF2", false, ["deriveKey"]);
    const protectionKey = await webcrypto.subtle.deriveKey({
      name: "PBKDF2",
      hash: "SHA-256",
      salt,
      iterations: pbkdf2Iterations
    }, material, { name: "AES-GCM", length: 256 }, false, ["decrypt"]);
    const privateKeyPkcs8 = new Uint8Array(await webcrypto.subtle.decrypt({
      name: "AES-GCM",
      iv,
      additionalData: aad,
      tagLength: 128
    }, protectionKey, ciphertext));
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

function parseRecoveryKey(recoveryKey) {
  const trimmed = recoveryKey.trim();
  if (!trimmed.startsWith(recoveryPrefix) || trimmed.length > 10_000) {
    throw new Error("A chave de recuperação não pertence ao RpaBlockly Recorder.");
  }
  const encoded = trimmed.slice(recoveryPrefix.length);
  if (!/^[A-Za-z0-9_-]+$/u.test(encoded)) throw new Error("A chave de recuperação está corrompida.");
  let payload;
  try {
    payload = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(Buffer.from(encoded, "base64url")));
  } catch {
    throw new Error("A chave de recuperação está corrompida.");
  }
  if (payload?.schemaVersion !== 1 || payload.type !== recoveryType ||
      typeof payload.keyId !== "string" || !/^generated-[A-Za-z0-9_-]{24}$/u.test(payload.keyId) ||
      payload.keyAlgorithm !== "RSA-OAEP-SHA-256" || payload.modulusLength !== rsaModulusLength ||
      payload.protection?.algorithm !== "PBKDF2-HMAC-SHA-256+AES-256-GCM" ||
      payload.protection.iterations !== pbkdf2Iterations || !isBase64(payload.protection.salt, 16) ||
      !isBase64(payload.protection.iv, 12) || !isBase64(payload.protection.ciphertext, undefined, 512, 8_192)) {
    throw new Error("A chave de recuperação usa um contrato inválido.");
  }
  return payload;
}

function validatePassword(password) {
  if (password.length < 12 || password.length > 128 || password.trim() !== password) {
    throw new Error("A senha deve ter de 12 a 128 caracteres e não pode começar ou terminar com espaço.");
  }
  if (!/\p{L}/u.test(password) || !/\p{N}/u.test(password)) {
    throw new Error("A senha deve conter ao menos uma letra e um número.");
  }
}

function isBase64(value, exactBytes, minimumBytes, maximumBytes) {
  if (typeof value !== "string" || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value)) {
    return false;
  }
  const size = Buffer.from(value, "base64").length;
  return (exactBytes === undefined || size === exactBytes) &&
    (minimumBytes === undefined || size >= minimumBytes) &&
    (maximumBytes === undefined || size <= maximumBytes);
}

function formatPem(label, bytes) {
  const lines = Buffer.from(bytes).toString("base64").match(/.{1,64}/gu) ?? [];
  return `-----BEGIN ${label}-----\n${lines.join("\n")}\n-----END ${label}-----`;
}

async function readHiddenPassword() {
  if (!process.stdin.isTTY) {
    let value = "";
    process.stdin.setEncoding("utf8");
    for await (const chunk of process.stdin) value += chunk;
    return value.replace(/\r?\n$/u, "");
  }
  process.stdout.write("Senha de compartilhamento: ");
  process.stdin.setRawMode(true);
  process.stdin.resume();
  process.stdin.setEncoding("utf8");
  return await new Promise((resolve, reject) => {
    let value = "";
    const finish = (error) => {
      process.stdin.setRawMode(false);
      process.stdin.pause();
      process.stdin.removeListener("data", onData);
      process.stdout.write("\n");
      if (error) reject(error);
      else resolve(value);
    };
    const onData = (chunk) => {
      if (chunk === "\u0003") {
        finish(new Error("Operação cancelada."));
      } else if (chunk === "\r" || chunk === "\n") {
        finish();
      } else if (chunk === "\u0008" || chunk === "\u007f") {
        value = Array.from(value).slice(0, -1).join("");
      } else if (!/[\u0000-\u001f\u007f]/u.test(chunk)) {
        value += chunk;
      }
    };
    process.stdin.on("data", onData);
  });
}
