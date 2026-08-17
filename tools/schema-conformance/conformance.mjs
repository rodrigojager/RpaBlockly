import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const currentDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = join(currentDirectory, "..", "..");
const cases = [
  ["flow-v2.schema.json", "package-valid/flow.production.json", true],
  ["locators-v1.schema.json", "package-valid/locators.production.json", true],
  ["rpa-policy-v1.schema.json", "package-valid/rpa.policy.json", true],
  ["flow-v2.schema.json", "flow-invalid-unknown-property.json", false],
  ["flow-v2.schema.json", "flow-invalid-selector-embedded.json", false],
  ["locators-v1.schema.json", "locators-invalid-missing-target.json", false],
  ["locators-v1.schema.json", "locators-invalid-dual-text-source.json", false],
  ["locators-v1.schema.json", "locators-invalid-strategy-fields.json", false],
  ["rpa-policy-v1.schema.json", "policy-invalid-mode.json", false]
];

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
const validators = new Map();
for (const [schemaFile, fixtureFile, expectedValid] of cases) {
  let validate = validators.get(schemaFile);
  if (!validate) {
    const schema = JSON.parse(await readFile(
      join(repositoryRoot, "schemas", schemaFile),
      "utf8"));
    validate = ajv.compile(schema);
    validators.set(schemaFile, validate);
  }

  const instance = JSON.parse(await readFile(join(
    repositoryRoot,
    "tests",
    "RpaFlow.ContractsChecks",
    "Fixtures",
    fixtureFile), "utf8"));
  const valid = validate(instance);
  if (valid !== expectedValid) {
    throw new Error(
      `${fixtureFile}: esperado ${expectedValid}, obtido ${valid}: ` +
      ajv.errorsText(validate.errors));
  }

  console.log(`OK: ${fixtureFile} => ${expectedValid ? "válido" : "inválido"}.`);
}
