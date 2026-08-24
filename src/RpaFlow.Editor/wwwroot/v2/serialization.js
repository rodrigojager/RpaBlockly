import {
  definitionForAction,
  definitionForBlock
} from "./action-catalog.js";
import { cardinalityFieldName, fieldName } from "./toolbox.js";

const locatorRoles = [
  "target", "trigger", "options", "ready", "success", "protocol"
];

export function loadFlow(workspace, flow) {
  Blockly.Events.disable();
  try {
    workspace.clear();
    const main = createChain(workspace, flow.actions ?? []);
    if (main) main.moveBy(40, 45);
    let index = 0;
    for (const [name, actions] of Object.entries(flow.subflows ?? {})) {
      const block = workspace.newBlock("rpa_subflow_definition");
      block.setFieldValue(name, "SUBFLOW");
      block.initSvg();
      block.render();
      const child = createChain(workspace, actions);
      if (child) block.getInput("ACTIONS").connection.connect(child.previousConnection);
      block.moveBy(700, 45 + (index * 210));
      index += 1;
    }
  } finally {
    Blockly.Events.enable();
  }
  workspace.render();
}

export function readFlow(workspace, template) {
  const roots = workspace.getTopBlocks(true);
  const definitions = roots.filter(block => block.type === "rpa_subflow_definition");
  const mainRoots = roots.filter(block => block.type !== "rpa_subflow_definition");
  if (mainRoots.length !== 1) {
    throw new Error("O fluxo principal deve possuir exatamente uma sequência de blocos.");
  }

  const subflows = {};
  for (const block of definitions) {
    const name = required(block.getFieldValue("SUBFLOW"), "nome do subfluxo");
    if (Object.keys(subflows).some(item => item.toLowerCase() === name.toLowerCase())) {
      throw new Error(`O subfluxo '${name}' está duplicado.`);
    }
    subflows[name] = readChain(block.getInputTargetBlock("ACTIONS"));
  }

  return {
    schemaVersion: 2,
    name: template?.name ?? "Fluxo V2",
    inputs: structuredClone(template?.inputs ?? []),
    actions: readChain(mainRoots[0]),
    subflows
  };
}

export function actionFromBlock(block) {
  const definition = definitionForBlock(block.type);
  if (!definition?.actionType) throw new Error(`Bloco não suportado: ${block.type}.`);
  const action = parseData(block.data);
  action.id = required(block.getFieldValue("ID"), "ID da ação");
  action.type = definition.actionType;
  action.name = required(block.getFieldValue("NAME"), "nome da ação");

  if (definition.actionType === "download") action.downloadMode = definition.variant;
  if (definition.actionType === "if") {
    action.condition = action.condition ?? {};
    action.condition.type = definition.variant;
  }

  for (const role of definition.roles) {
    const locatorId = block.getFieldValue(fieldName(role));
    const use = locatorId
      ? {
          locatorId,
          cardinality: block.getFieldValue(cardinalityFieldName(role)) || "single"
        }
      : null;
    if (role === "condition") {
      action.condition = action.condition ?? { type: "element" };
      action.condition.locator = use;
    } else {
      action[role] = use;
    }
  }

  if (["if", "repeat", "forEach"].includes(definition.structural)) {
    action.actions = readChain(block.getInputTargetBlock("ACTIONS"));
  }
  if (definition.structural === "if") {
    action.elseActions = readChain(block.getInputTargetBlock("ELSE_ACTIONS"));
  }
  return removeNulls(action);
}

export function editableProperties(block) {
  const value = actionFromBlock(block);
  delete value.id;
  delete value.type;
  delete value.name;
  delete value.actions;
  delete value.elseActions;
  for (const role of locatorRoles) delete value[role];
  if (value.condition) delete value.condition.locator;
  return value;
}

export function applyEditableProperties(block, properties) {
  if (!properties || Array.isArray(properties) || typeof properties !== "object") {
    throw new Error("As propriedades da ação devem formar um objeto JSON.");
  }
  const forbidden = ["id", "type", "name", "actions", "elseActions", ...locatorRoles];
  const found = forbidden.find(name => Object.hasOwn(properties, name));
  if (found) {
    throw new Error(`A propriedade '${found}' é editada pelo bloco, não pelo JSON avançado.`);
  }
  if (properties.condition?.locator) {
    throw new Error("condition.locator deve ser escolhido pelo FieldLocatorReference.");
  }
  block.data = JSON.stringify(structuredClone(properties));
}

function createChain(workspace, actions) {
  let first = null;
  let previous = null;
  for (const action of actions) {
    const block = createActionBlock(workspace, action);
    if (!first) first = block;
    if (previous) previous.nextConnection.connect(block.previousConnection);
    previous = block;
  }
  return first;
}

function createActionBlock(workspace, action) {
  const definition = definitionForAction(action);
  if (!definition) throw new Error(`Ação V2 sem bloco: '${action.type}'.`);
  const block = workspace.newBlock(definition.blockType);
  block.data = JSON.stringify(baseProperties(action));
  block.setFieldValue(action.id, "ID");
  block.setFieldValue(action.name, "NAME");
  for (const role of definition.roles) {
    const use = role === "condition" ? action.condition?.locator : action[role];
    if (!use) continue;
    block.setFieldValue(use.locatorId, fieldName(role));
    block.setFieldValue(use.cardinality ?? "single", cardinalityFieldName(role));
  }
  block.initSvg();
  block.render();
  if (["if", "repeat", "forEach"].includes(definition.structural)) {
    const nested = createChain(workspace, action.actions ?? []);
    if (nested) block.getInput("ACTIONS").connection.connect(nested.previousConnection);
  }
  if (definition.structural === "if") {
    const nested = createChain(workspace, action.elseActions ?? []);
    if (nested) block.getInput("ELSE_ACTIONS").connection.connect(nested.previousConnection);
  }
  return block;
}

function readChain(first) {
  const actions = [];
  let current = first;
  while (current) {
    actions.push(actionFromBlock(current));
    current = current.getNextBlock();
  }
  return actions;
}

function baseProperties(action) {
  const value = structuredClone(action);
  delete value.id;
  delete value.type;
  delete value.name;
  delete value.actions;
  delete value.elseActions;
  for (const role of locatorRoles) delete value[role];
  if (value.condition) delete value.condition.locator;
  return value;
}

function parseData(value) {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value);
    return parsed && !Array.isArray(parsed) && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
}

function required(value, description) {
  const text = String(value ?? "").trim();
  if (!text) throw new Error(`Preencha ${description}.`);
  return text;
}

function removeNulls(value) {
  for (const key of Object.keys(value)) {
    if (value[key] === null || value[key] === undefined) delete value[key];
  }
  return value;
}
