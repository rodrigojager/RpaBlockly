import Ajv2020, { type ErrorObject, type ValidateFunction } from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import flowSchema from "../../../../schemas/flow-v2.schema.json" with { type: "json" };
import locatorSchema from "../../../../schemas/locators-v1.schema.json" with { type: "json" };
import policySchema from "../../../../schemas/rpa-policy-v1.schema.json" with { type: "json" };
import type { GeneratedPackage } from "./generator.js";

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
const validators = {
  flow: ajv.compile(flowSchema),
  locators: ajv.compile(locatorSchema),
  policy: ajv.compile(policySchema)
};

export function validateGeneratedPackage(result: GeneratedPackage): void {
  validate("flow.production.json", validators.flow, result.flow);
  validate("locators.production.json", validators.locators, result.locators);
  validate("rpa.policy.json", validators.policy, result.policy);
  validateSemantics(result);
}

function validate(name: string, validator: ValidateFunction, value: unknown): void {
  if (!validator(value)) {
    throw new Error(`${name} inválido: ${formatErrors(validator.errors)}`);
  }
}

function validateSemantics(result: GeneratedPackage): void {
  const locatorIds = new Set(result.locators.locators.map((locator) => locator.id));
  const actionIds = new Set<string>();
  for (const action of result.flow.actions) {
    if (actionIds.has(action.id)) throw new Error(`ID de ação duplicado: ${action.id}.`);
    actionIds.add(action.id);
    for (const use of [action.target, action.trigger, action.options, action.ready, action.success, action.protocol]) {
      if (use !== undefined && !locatorIds.has(use.locatorId)) {
        throw new Error(`A ação ${action.id} referencia locator ausente: ${use.locatorId}.`);
      }
    }
    if (action.type === "clickAndSwitchPage" && action.ready === undefined) {
      throw new Error(`A ação ${action.id} exige locator ready.`);
    }
  }
  if (result.policy.locatorResilience.mode !== "strict" ||
      result.policy.locatorResilience.promotion !== "disabled" ||
      result.policy.locatorResilience.learningWriteBack !== "disabled") {
    throw new Error("O pacote do Recorder deve usar política strict conservadora.");
  }
}

function formatErrors(errors: ErrorObject[] | null | undefined): string {
  return errors?.map((error) => `${error.instancePath || "/"} ${error.message ?? "inválido"}`).join("; ")
    ?? "erro desconhecido";
}
