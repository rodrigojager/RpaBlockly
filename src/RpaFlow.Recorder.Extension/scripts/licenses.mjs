import { readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const lock = JSON.parse(await readFile(join(root, "package-lock.json"), "utf8"));
const packages = Object.entries(lock.packages)
  .filter(([path, value]) => path.startsWith("node_modules/") && value.version)
  .map(([path, value]) => ({
    name: path.slice("node_modules/".length),
    version: value.version,
    license: value.license ?? "não declarada"
  }))
  .sort((left, right) => left.name.localeCompare(right.name));
const rows = packages.map((item) =>
  `| \`${item.name}\` | \`${item.version}\` | ${item.license} |`).join("\n");
const document = `# Avisos de terceiros — Recorder V2

Inventário gerado de \`package-lock.json\`. As dependências de produção são
empacotadas localmente; a extensão não carrega código remoto.

| Pacote | Versão | Licença declarada |
|---|---:|---|
${rows}

Este inventário não substitui os textos de licença distribuídos pelos autores.
`;
const output = join(root, "THIRD_PARTY_NOTICES.md");
if (process.argv.includes("--verify")) {
  const current = await readFile(output, "utf8");
  if (current !== document) throw new Error("THIRD_PARTY_NOTICES.md está desatualizado.");
} else {
  await writeFile(output, document, "utf8");
}
console.log(`Inventário de ${packages.length} pacotes atualizado.`);
