import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import standaloneCode from "ajv/dist/standalone/index.js";
import { build } from "esbuild";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const schemaDirectory = join(root, "..", "..", "schemas");
const output = join(root, "src", "package", "generated", "schema-validators.ts");
const definitions = [
  ["validateFlow", "flow-v2.schema.json"],
  ["validateLocators", "locators-v1.schema.json"],
  ["validatePolicy", "rpa-policy-v1.schema.json"]
];
const schemas = new Map();

for (const [exportName, fileName] of definitions) {
  const schema = JSON.parse(await readFile(join(schemaDirectory, fileName), "utf8"));
  schemas.set(exportName, schema);
}

const ajv = new Ajv2020({
  allErrors: true,
  strict: true,
  code: { esm: true, lines: true, source: true }
});
addFormats(ajv);
for (const schema of schemas.values()) ajv.addSchema(schema);

const exports = Object.fromEntries(
  [...schemas].map(([exportName, schema]) => [exportName, schema.$id])
);
const standalone = standaloneCode(ajv, exports);
const bundled = await build({
  bundle: true,
  charset: "utf8",
  format: "esm",
  legalComments: "none",
  minify: false,
  platform: "browser",
  stdin: {
    contents: standalone,
    loader: "js",
    resolveDir: root,
    sourcefile: "schema-validators.generated.js"
  },
  target: "chrome116",
  treeShaking: true,
  write: false
});
const generated = bundled.outputFiles[0]?.text;
if (generated === undefined) throw new Error("O bundle dos validadores não foi produzido.");
const header = [
  "// Arquivo gerado por scripts/generate-schema-validators.mjs.",
  "// Não edite manualmente; altere os schemas e gere novamente.",
  "// @ts-nocheck",
  ""
].join("\n");

await mkdir(dirname(output), { recursive: true });
await writeFile(output, `${header}${generated}\n`, "utf8");
console.log(`Validadores CSP-safe gerados em ${output}`);
