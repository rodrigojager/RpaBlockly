import type { ErrorObject } from "ajv";
import type { GeneratedPackage } from "./generator.js";
import {
  validateFlow,
  validateLocators,
  validatePolicy
} from "./generated/schema-validators.js";

interface StandaloneValidateFunction {
  (value: unknown): boolean;
  errors?: ErrorObject[] | null;
}

const validators: Record<"flow" | "locators" | "policy", StandaloneValidateFunction> = {
  flow: validateFlow,
  locators: validateLocators,
  policy: validatePolicy
};

export function validateGeneratedPackage(result: GeneratedPackage): void {
  validate("flow.production.json", validators.flow, result.flow);
  validate("locators.production.json", validators.locators, result.locators);
  validate("rpa.policy.json", validators.policy, result.policy);
  validateSemantics(result);
}

function validate(name: string, validator: StandaloneValidateFunction, value: unknown): void {
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
