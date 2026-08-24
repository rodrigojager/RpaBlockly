import { actionTypes } from "./action-catalog.js";

const forbiddenLocatorProperties = [
  "selector", "scope", "scopeHasText", "scopeHasTextSource", "hasText",
  "hasTextSource", "frameSelectors", "matchMode", "triggerSelector",
  "optionSelector", "readySelector", "successSelector", "protocolSelector"
];

export function validatePackageLocally(flow, locators, policy) {
  const errors = [];
  const warnings = [];
  if (flow?.schemaVersion !== 2) errors.push("flow.schemaVersion deve ser 2.");
  if (locators?.schemaVersion !== 1) errors.push("locators.schemaVersion deve ser 1.");
  if (policy?.schemaVersion !== 1) errors.push("policy.schemaVersion deve ser 1.");
  const definitions = new Map();
  for (const locator of locators?.locators ?? []) {
    const key = String(locator.id ?? "").toLowerCase();
    if (!key) errors.push("Todo locator deve possuir ID.");
    else if (definitions.has(key)) errors.push(`Locator duplicado: '${locator.id}'.`);
    else definitions.set(key, locator);
    if (!(locator.candidates?.length > 0)) {
      errors.push(`O locator '${locator.id}' não possui candidato executável.`);
    }
  }

  const used = new Set();
  const ids = new Set();
  for (const action of enumerateActions(flow)) {
    const id = String(action.id ?? "").toLowerCase();
    if (!id) errors.push("Toda ação deve possuir ID.");
    else if (ids.has(id)) errors.push(`Ação duplicada: '${action.id}'.`);
    else ids.add(id);
    if (!actionTypes.includes(action.type)) errors.push(`Tipo não suportado: '${action.type}'.`);
    for (const property of forbiddenLocatorProperties) {
      if (Object.hasOwn(action, property)) {
        errors.push(`A ação '${action.id}' contém o seletor V1 proibido '${property}'.`);
      }
    }
    for (const [role, use] of locatorUses(action)) {
      if (!use) continue;
      const key = String(use.locatorId ?? "").toLowerCase();
      used.add(key);
      if (!definitions.has(key)) {
        errors.push(`Ação '${action.id}' referencia locator ausente '${use.locatorId}'.`);
      }
      if (["trigger", "ready", "success", "protocol", "condition"].includes(role) &&
          use.cardinality === "many") {
        errors.push(`O papel '${role}' de '${action.id}' não aceita cardinalidade many.`);
      }
      if (role === "options" && use.cardinality !== "many") {
        errors.push(`As opções de '${action.id}' exigem cardinalidade many.`);
      }
    }
  }
  for (const locator of locators?.locators ?? []) {
    if (!used.has(String(locator.id).toLowerCase())) {
      warnings.push(`Locator não utilizado: ${locator.id}.`);
    }
  }
  return { errors, warnings };
}

export function enumerateActions(flow) {
  const result = [];
  const visit = actions => {
    for (const action of actions ?? []) {
      result.push(action);
      visit(action.actions);
      visit(action.elseActions);
    }
  };
  visit(flow?.actions);
  for (const actions of Object.values(flow?.subflows ?? {})) visit(actions);
  return result;
}

function locatorUses(action) {
  return [
    ["target", action.target],
    ["trigger", action.trigger],
    ["options", action.options],
    ["ready", action.ready],
    ["success", action.success],
    ["protocol", action.protocol],
    ["condition", action.condition?.locator]
  ];
}
