import { readFile, readdir } from "node:fs/promises";
import { dirname, extname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(await readFile(join(root, "manifest.json"), "utf8"));
const expectedPermissions = ["activeTab", "scripting", "storage", "downloads", "sidePanel"];
const forbiddenPermissions = ["debugger", "webRequest", "nativeMessaging", "cookies", "tabs"];
if (JSON.stringify(manifest.permissions) !== JSON.stringify(expectedPermissions)) {
  throw new Error("O manifest não contém exatamente as permissões mínimas aprovadas.");
}
if (forbiddenPermissions.some((permission) => manifest.permissions.includes(permission))) {
  throw new Error("O manifest contém uma permissão bloqueada pelo threat model.");
}
if (JSON.stringify(manifest.optional_permissions) !== JSON.stringify(["tabs"])) {
  throw new Error("O manifest deve declarar somente tabs como permissão opcional.");
}
if ((manifest.host_permissions ?? []).length !== 0) {
  throw new Error("Acesso permanente a hosts não é permitido.");
}
if (JSON.stringify(manifest.optional_host_permissions) !== JSON.stringify(["<all_urls>"])) {
  throw new Error("A captura visual deve declarar somente <all_urls> como host opcional.");
}
if (manifest.minimum_chrome_version !== "116" || manifest.manifest_version !== 3) {
  throw new Error("A extensão deve usar MV3 e Chrome 116 como versão mínima.");
}
const csp = manifest.content_security_policy?.extension_pages ?? "";
if (!csp.includes("script-src 'self'") || csp.includes("unsafe-eval") || /^https?:/m.test(csp)) {
  throw new Error("A CSP da extensão permite código remoto ou avaliação dinâmica.");
}

const decoder = new TextDecoder("utf-8", { fatal: true });
const mojibake = new RegExp("[\\u00c3\\u00c2].|\\u00e2[\\u0080-\\u00bf\\u20ac].?", "u");
for (const path of await walk(root)) {
  if (![".ts", ".mjs", ".json", ".html", ".css", ".md"].includes(extname(path)) ||
      path.includes(`${join(root, "node_modules")}`) || path.includes(`${join(root, "build")}`)) {
    continue;
  }
  const text = decoder.decode(await readFile(path));
  if (mojibake.test(text)) throw new Error(`Mojibake detectado em ${relative(root, path)}.`);
  if (/https?:\/\/[^\s"']+\.js(?:\?|["'])/u.test(text)) {
    throw new Error(`Código remoto detectado em ${relative(root, path)}.`);
  }
}
console.log("Manifest, CSP, permissões e UTF-8 validados.");

async function walk(directory) {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && ["node_modules", "build", ".test-build"].includes(entry.name)) continue;
    const path = join(directory, entry.name);
    if (entry.isDirectory()) result.push(...await walk(path));
    else result.push(path);
  }
  return result;
}
