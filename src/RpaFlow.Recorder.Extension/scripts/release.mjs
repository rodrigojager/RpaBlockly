import { createHash } from "node:crypto";
import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { dirname, join, relative, sep } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { zipSync } from "fflate";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const repositoryRoot = join(root, "..", "..");
const version = JSON.parse(await readFile(join(root, "package.json"), "utf8")).version;
const fileName = `rpablockly-recorder-${version}.zip`;
const artifactDirectory = join(repositoryRoot, "artifacts");
const checksumDirectory = join(root, "release");

const first = await buildAndZip();
const second = await buildAndZip();
if (!first.equals(second)) {
  throw new Error("O build da extensão não é reproduzível byte a byte.");
}
const sha256 = createHash("sha256").update(first).digest("hex");
await mkdir(artifactDirectory, { recursive: true });
await mkdir(checksumDirectory, { recursive: true });
await writeFile(join(artifactDirectory, fileName), first);
const checksumPath = join(checksumDirectory, `${fileName}.sha256`);
const checksum = `${sha256}  ${fileName}\n`;
if (process.argv.includes("--verify")) {
  const current = await readFile(checksumPath, "utf8");
  if (current !== checksum) {
    throw new Error("O checksum versionado não corresponde ao build reproduzível.");
  }
} else {
  await writeFile(checksumPath, checksum, "utf8");
}
console.log(`${fileName}: ${sha256}`);

async function buildAndZip() {
  const result = spawnSync(process.execPath, ["scripts/build.mjs"], {
    cwd: root,
    encoding: "utf8",
    stdio: "inherit"
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
  const output = join(root, "build");
  const entries = {};
  for (const path of await walk(output)) {
    const name = relative(output, path).split(sep).join("/");
    entries[name] = [new Uint8Array(await readFile(path)), {
      mtime: new Date(1980, 0, 1, 0, 0, 0, 0)
    }];
  }
  return Buffer.from(zipSync(entries, { level: 9 }));
}

async function walk(directory) {
  const result = [];
  const children = await readdir(directory, { withFileTypes: true });
  for (const child of children.sort((left, right) => left.name.localeCompare(right.name))) {
    const path = join(directory, child.name);
    if (child.isDirectory()) result.push(...await walk(path));
    else if (child.isFile()) result.push(path);
    else throw new Error(`Tipo de entrada não suportado no build: ${path}.`);
  }
  return result;
}
