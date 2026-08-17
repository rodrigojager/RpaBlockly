import { mkdir, readdir, rm } from "node:fs/promises";
import { dirname, join } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const output = join(root, ".test-build");
await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
const entries = (await readdir(join(root, "test"), { recursive: true }))
  .filter((path) => path.endsWith(".test.ts"))
  .map((path) => join(root, "test", path));
await build({
  bundle: true,
  entryPoints: entries,
  format: "esm",
  outdir: output,
  packages: "external",
  platform: "node",
  sourcemap: false,
  target: "node24"
});
const builtTests = (await readdir(output, { recursive: true }))
  .filter((path) => path.endsWith(".test.js"))
  .map((path) => join(output, path));
const result = spawnSync(process.execPath, ["--test", ...builtTests], {
  cwd: root,
  encoding: "utf8",
  stdio: "inherit"
});
if (result.status !== 0) process.exit(result.status ?? 1);
