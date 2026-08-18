import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";
import { assertNoDynamicCode } from "./csp.mjs";
import "./generate-schema-validators.mjs";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const output = join(root, "build");
await rm(output, { recursive: true, force: true });
await mkdir(join(output, "sidepanel"), { recursive: true });

await build({
  absWorkingDir: root,
  bundle: true,
  charset: "utf8",
  entryPoints: {
    "background/service-worker": "src/background/service-worker.ts",
    "content/content-script": "src/content/content-script.ts",
    "sidepanel/sidepanel": "src/sidepanel/sidepanel.ts"
  },
  format: "esm",
  legalComments: "none",
  minify: false,
  outdir: output,
  platform: "browser",
  sourcemap: false,
  target: "chrome116",
  treeShaking: true
});

await cp(join(root, "manifest.json"), join(output, "manifest.json"));
await cp(join(root, "src", "sidepanel", "index.html"), join(output, "sidepanel", "index.html"));
await cp(join(root, "src", "sidepanel", "styles.css"), join(output, "sidepanel", "styles.css"));

const manifest = JSON.parse(await readFile(join(output, "manifest.json"), "utf8"));
await writeFile(
  join(output, "build-info.json"),
  `${JSON.stringify({ name: manifest.name, version: manifest.version }, null, 2)}\n`,
  "utf8"
);
await assertNoDynamicCode(output);
console.log(`Extensão gerada em ${output}`);
