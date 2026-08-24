import {
  connect,
  fetchPackage,
  openPackage,
  readConfiguration,
  RevisionConflictError,
  saveConfiguration,
  savePackage
} from "./api.js";
import { actionCatalog } from "./action-catalog.js";
import { initializeAssistedValidation } from "./assisted-validation.js";
import { initializeConfigurationUi } from "./configuration-ui.js";
import { setLocatorProvider } from "./field-locator-reference.js";
import { initializeLocatorUi } from "./locator-ui.js";
import { initializePolicyUi, renderPolicyUi } from "./policy-ui.js";
import { initializeRecorderImport } from "./recorder-import.js";
import {
  actionFromBlock,
  applyEditableProperties,
  editableProperties,
  loadFlow,
  readFlow
} from "./serialization.js";
import { editorState, updateState } from "./state.js";
import { createToolbox, registerBlocks } from "./toolbox.js";
import { validatePackageLocally } from "./validation.js";

if (!window.Blockly) {
  document.body.textContent = "Não foi possível carregar o Blockly local.";
  throw new Error("Blockly indisponível.");
}

registerBlocks();
setLocatorProvider(() => editorState.package?.locators?.locators ?? []);
const workspace = Blockly.inject("blockly-editor", {
  toolbox: createToolbox(),
  media: "vendor/blockly/media/",
  renderer: "zelos",
  trashcan: true,
  move: { scrollbars: true, drag: true, wheel: true },
  zoom: { controls: true, wheel: true, startScale: 0.72, maxScale: 1.4, minScale: 0.35 },
  grid: { spacing: 20, length: 3, colour: "#d9e2ec", snap: true }
});

const generatedJson = document.getElementById("generated-json");
const validationMessage = document.getElementById("validation-message");
const serverStatus = document.getElementById("server-status");
const packageIdentity = document.getElementById("package-identity");
const warningsList = document.getElementById("package-warnings");
const propertiesEditor = document.getElementById("action-properties-json");
const propertiesTitle = document.getElementById("action-properties-title");
const conflictPanel = document.getElementById("revision-conflict");
const compareDialog = document.getElementById("compare-dialog");
const compareContent = document.getElementById("compare-content");
let changeTimer = null;
let conflictDraft = null;
let highlightedActionId = null;

const locatorUi = initializeLocatorUi(
  refresh,
  error => showMessage(error.message, true));

initializeRecorderImport(result => {
  updateState({
    package: {
      rpaId: result.rpaId,
      revision: result.revision,
      contentHash: result.contentHash,
      origin: { kind: "recorder-import", location: result.evidenceArchive },
      flow: result.flow,
      locators: result.locators,
      policy: result.policy,
      warnings: result.warnings ?? []
    },
    conflict: null
  });
  loadFlow(workspace, result.flow);
  locatorUi.render();
  renderPolicy();
  refresh();
}, message => showMessage(message));

workspace.addChangeListener(event => {
  if (event.type === Blockly.Events.SELECTED) {
    selectBlock(event.newElementId ? workspace.getBlockById(event.newElementId) : null);
  }
  if (event.isUiEvent) return;
  window.clearTimeout(changeTimer);
  changeTimer = window.setTimeout(refresh, 80);
});

document.getElementById("save-package").addEventListener("click", saveCurrentPackage);
document.getElementById("reload-package").addEventListener("click", reloadCurrentPackage);
document.getElementById("reset-flow").addEventListener("click", () => {
  if (editorState.package) loadFlow(workspace, editorState.package.flow);
  refresh();
});
document.getElementById("save-action-properties").addEventListener("click", () => {
  try {
    if (!editorState.selectedBlock) throw new Error("Selecione uma ação.");
    const value = JSON.parse(propertiesEditor.value || "{}");
    applyEditableProperties(editorState.selectedBlock, value);
    refresh();
    showMessage("Propriedades avançadas aplicadas ao bloco. Salve o pacote para persistir.");
  } catch (error) {
    showMessage(error.message, true);
  }
});
initializePolicyUi(value => {
  try {
    const packageValue = structuredClone(editorState.package);
    packageValue.policy = value;
    const validation = validatePackageLocally(
      packageValue.flow,
      packageValue.locators,
      packageValue.policy);
    if (validation.errors.length) throw new Error(validation.errors.join("\n"));
    updateState({ package: packageValue });
    renderPolicy();
    refresh();
    showMessage("Política aplicada ao rascunho. Salve o pacote para persistir.");
  } catch (error) {
    showMessage(error.message, true);
  }
}, error => showMessage(error.message, true));
document.getElementById("compare-revision").addEventListener("click", compareRevision);
document.getElementById("save-new-revision").addEventListener("click", saveAfterExplicitCompare);
document.getElementById("export-package").addEventListener("click", exportPackage);
initializeConfigurationUi({
  fields: () => editorState.session?.profile?.configurationFields ?? [],
  load: readConfiguration,
  save: saveConfiguration,
  onMessage: showMessage
});
initializeAssistedValidation({
  documents: currentDocuments,
  revision: () => editorState.package?.revision,
  onAction: actionId => highlightAction(actionId),
  onMessage: showMessage
});

try {
  const session = await connect();
  const packageValue = await openPackage();
  document.getElementById("editor-title").textContent = session.profile.displayName;
  loadFlow(workspace, packageValue.flow);
  locatorUi.render();
  renderPolicy();
  serverStatus.textContent = "Pacote V2 conectado";
  serverStatus.classList.add("connected");
  refresh();
} catch (error) {
  serverStatus.textContent = "Falha ao abrir pacote V2";
  serverStatus.classList.add("error");
  showMessage(error.message, true);
}

if (new URLSearchParams(window.location.search).has("roundtrip-test")) {
  let testingLocatorBlock = null;
  window.RpaFlowEditorTesting = {
    roundTrip(flow, locators, policy) {
      const temporaryPackage = {
        flow: structuredClone(flow),
        locators: structuredClone(locators),
        policy: structuredClone(policy)
      };
      const previous = editorState.package;
      updateState({ package: temporaryPackage });
      loadFlow(workspace, flow);
      const result = readFlow(workspace, flow);
      updateState({ package: previous });
      return result;
    },
    toolboxBlockTypes() {
      return actionCatalog.map(item => item.blockType);
    },
    instantiateAllBlocks() {
      return actionCatalog.map(definition => {
        const block = workspace.newBlock(definition.blockType);
        block.initSvg();
        const fields = block.inputList.flatMap(input =>
          input.fieldRow.map(field => field.name).filter(Boolean));
        block.dispose(false);
        return { type: definition.blockType, fields };
      });
    },
    packageValidation(flow, locators, policy) {
      return validatePackageLocally(flow, locators, policy);
    },
    setPackage(flow, locators, policy) {
      updateState({
        package: {
          ...(editorState.package ?? {}),
          flow: structuredClone(flow),
          locators: structuredClone(locators),
          policy: structuredClone(policy)
        }
      });
      loadFlow(workspace, flow);
      locatorUi.render();
      renderPolicy();
      refresh();
    },
    openLocatorPicker() {
      testingLocatorBlock?.dispose(false);
      testingLocatorBlock = workspace.newBlock("rpa_click");
      testingLocatorBlock.initSvg();
      testingLocatorBlock.render();
      testingLocatorBlock.getField("LOCATOR_TARGET").showEditor_();
    },
    locatorPickerValue() {
      const value = testingLocatorBlock?.getFieldValue("LOCATOR_TARGET") ?? null;
      testingLocatorBlock?.dispose(false);
      testingLocatorBlock = null;
      return value;
    },
    packagePolicy() {
      return structuredClone(editorState.package?.policy ?? null);
    },
    highlightedActionId() {
      return highlightedActionId;
    }
  };
}

function refresh() {
  if (!editorState.package || workspace.getAllBlocks(false).length === 0) return;
  try {
    const flow = readFlow(workspace, editorState.package.flow);
    editorState.package.flow = flow;
    const result = validatePackageLocally(
      flow,
      editorState.package.locators,
      editorState.package.policy);
    generatedJson.textContent = JSON.stringify(flow, null, 2);
    renderIdentity();
    renderWarnings(result.warnings);
    if (result.errors.length) showMessage(result.errors.join("\n"), true);
    else showMessage("Pacote coerente no navegador; o backend repetirá a validação oficial ao salvar.");
  } catch (error) {
    showMessage(error.message, true);
  }
}

async function saveCurrentPackage() {
  try {
    const documents = currentDocuments();
    const validation = validatePackageLocally(
      documents.flow,
      documents.locators,
      documents.policy);
    if (validation.errors.length) throw new Error(validation.errors.join("\n"));
    const saved = await savePackage(documents);
    conflictDraft = null;
    conflictPanel.hidden = true;
    locatorUi.render();
    renderPolicy();
    renderIdentity();
    renderWarnings(saved.warnings ?? []);
    showMessage(`Pacote salvo atomicamente na revisão ${short(saved.revision)}.`);
  } catch (error) {
    if (error instanceof RevisionConflictError) {
      conflictDraft = currentDocuments();
      conflictPanel.hidden = false;
    }
    showMessage(error.message, true);
  }
}

async function reloadCurrentPackage() {
  try {
    const packageValue = await openPackage();
    conflictDraft = null;
    conflictPanel.hidden = true;
    loadFlow(workspace, packageValue.flow);
    locatorUi.render();
    renderPolicy();
    refresh();
    showMessage("Revisão atual recarregada.");
  } catch (error) {
    showMessage(error.message, true);
  }
}

async function compareRevision() {
  try {
    const remote = await fetchPackage();
    compareContent.textContent = JSON.stringify({
      revisãoAberta: editorState.package?.revision,
      revisãoAtual: remote.revision,
      local: conflictDraft,
      atual: {
        flow: remote.flow,
        locators: remote.locators,
        policy: remote.policy
      }
    }, null, 2);
    compareDialog.showModal();
  } catch (error) {
    showMessage(error.message, true);
  }
}

async function saveAfterExplicitCompare() {
  try {
    if (!conflictDraft) throw new Error("Não existe revisão local em conflito.");
    const remote = await fetchPackage();
    const accepted = window.confirm(
      `A revisão atual é ${short(remote.revision)}. Publicar explicitamente o conteúdo local como nova revisão?`);
    if (!accepted) return;
    updateState({ package: remote });
    const saved = await savePackage(conflictDraft);
    updateState({ package: saved });
    conflictDraft = null;
    conflictPanel.hidden = true;
    loadFlow(workspace, saved.flow);
    locatorUi.render();
    renderPolicy();
    refresh();
  } catch (error) {
    showMessage(error.message, true);
  }
}

function currentDocuments() {
  return {
    flow: readFlow(workspace, editorState.package.flow),
    locators: structuredClone(editorState.package.locators),
    policy: structuredClone(editorState.package.policy)
  };
}

function selectBlock(block) {
  if (!block || block.type === "rpa_subflow_definition") {
    updateState({ selectedBlock: null });
    propertiesTitle.textContent = "Propriedades avançadas da ação";
    propertiesEditor.value = "";
    return;
  }
  try {
    actionFromBlock(block);
    updateState({ selectedBlock: block });
    propertiesTitle.textContent = `Propriedades: ${block.getFieldValue("NAME")}`;
    propertiesEditor.value = JSON.stringify(editableProperties(block), null, 2);
  } catch {
    updateState({ selectedBlock: null });
  }
}

function exportPackage() {
  const blob = new Blob([JSON.stringify(currentDocuments(), null, 2) + "\n"], {
    type: "application/json"
  });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `${editorState.package?.rpaId ?? "rpa"}.package.json`;
  link.click();
  URL.revokeObjectURL(link.href);
}

function renderIdentity() {
  if (!editorState.package) return;
  packageIdentity.textContent =
    `${editorState.package.rpaId} · revisão ${short(editorState.package.revision)} · ` +
    `${editorState.package.origin.kind}`;
}

function renderPolicy() {
  if (editorState.package?.policy) {
    renderPolicyUi(editorState.package.policy);
  }
}

function renderWarnings(warnings) {
  warningsList.replaceChildren();
  for (const warning of warnings) {
    const item = document.createElement("li");
    item.textContent = warning;
    warningsList.append(item);
  }
  warningsList.hidden = warnings.length === 0;
}

function showMessage(message, error = false) {
  validationMessage.textContent = message;
  validationMessage.classList.toggle("error", error);
}

function short(revision) {
  return String(revision ?? "").slice(0, 12);
}

function highlightAction(actionId) {
  workspace.highlightBlock();
  highlightedActionId = actionId ?? null;
  if (!actionId) return;
  const block = workspace.getAllBlocks(false).find(candidate =>
    candidate.getFieldValue("ID") === actionId);
  if (!block) return;
  workspace.highlightBlock(block.id);
  workspace.centerOnBlock(block.id);
}
