import { mkdir, rm } from "node:fs/promises";
import { dirname, join } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const [, , inputPath, outputPath] = process.argv;
if (inputPath === undefined || outputPath === undefined) {
  throw new Error("Uso: node scripts/export-captured.mjs <mensagens.json> <bundle.zip>.");
}
const temporary = join(root, ".test-build", "support");
const compiled = join(temporary, "export-captured.mjs");
await rm(temporary, { recursive: true, force: true });
await mkdir(temporary, { recursive: true });
await build({
  absWorkingDir: root,
  bundle: true,
  entryPoints: ["test/support/export-captured.ts"],
  format: "esm",
  outfile: compiled,
  packages: "external",
  platform: "node",
  sourcemap: false,
  target: "node24"
});
const result = spawnSync(process.execPath, [compiled, inputPath, outputPath], {
  cwd: root,
  encoding: "utf8",
  stdio: "inherit"
});
if (result.status !== 0) process.exit(result.status ?? 1);
