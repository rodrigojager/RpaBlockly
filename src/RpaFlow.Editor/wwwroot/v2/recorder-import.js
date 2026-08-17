import {
  applyRecorderImport,
  deleteRecorderImport,
  inspectRecorderBundle,
  recorderEvidence,
  validateRecorderImport
} from "./api.js";

export function initializeRecorderImport(onApplied, onMessage) {
  const dialog = document.getElementById("recorder-import-dialog");
  const status = document.getElementById("recorder-import-status");
  const review = document.getElementById("recorder-review");
  const mappings = document.getElementById("recorder-mappings");
  const confirmation = document.getElementById("recorder-confirmation");
  const validationPreview = document.getElementById("recorder-validation-preview");
  const applyButton = document.getElementById("apply-recorder-import");
  let staging = null;
  let preview = null;
  let evidenceIndex = 0;
  let evidenceUrl = null;

  document.getElementById("open-recorder-import").addEventListener("click", () => {
    reset(false);
    dialog.showModal();
  });
  document.getElementById("close-recorder-import").addEventListener("click", close);
  document.getElementById("inspect-recorder").addEventListener("click", inspect);
  document.getElementById("validate-recorder-import").addEventListener("click", validate);
  applyButton.addEventListener("click", apply);
  document.getElementById("recorder-mode").addEventListener("change", event => {
    document.getElementById("recorder-subflow-name").disabled = event.target.value !== "subflow";
    applyButton.disabled = true;
  });
  document.getElementById("previous-recorder-evidence").addEventListener("click", () => {
    void showEvidence(evidenceIndex - 1);
  });
  document.getElementById("next-recorder-evidence").addEventListener("click", () => {
    void showEvidence(evidenceIndex + 1);
  });
  dialog.addEventListener("cancel", event => {
    event.preventDefault();
    void close();
  });

  async function inspect() {
    try {
      const file = document.getElementById("recorder-file").files[0];
      if (!file) throw new Error("Selecione um bundle Recorder.");
      setStatus("Verificando paths, limites, hashes e contratos…");
      if (staging) await deleteRecorderImport(staging.id, staging.token);
      const result = await inspectRecorderBundle(file);
      staging = { id: result.stagingId, token: result.stagingToken };
      preview = result.preview;
      renderPreview();
      review.hidden = false;
      mappings.hidden = false;
      confirmation.hidden = false;
      setStep("review");
      setStatus(`Bundle ${preview.bundleId} aceito em staging até ${new Date(result.expiresAtUtc).toLocaleString()}.`);
    } catch (error) {
      setStatus(error.message, true);
    }
  }

  function renderPreview() {
    const summary = document.getElementById("recorder-summary");
    summary.replaceChildren();
    for (const [label, value] of [
      ["Bundle", preview.bundleId], ["Origem", "chrome-recorder"],
      ["Destino", `${preview.targetRpaId} · ${short(preview.targetRevision)}`],
      ["Passos", preview.stepCount], ["Segredos", preview.hasSecrets ? "sim" : "não"],
      ["Uploads", preview.hasUploads ? "sim" : "não"]
    ]) {
      const term = document.createElement("dt");
      term.textContent = label;
      const description = document.createElement("dd");
      description.textContent = String(value);
      summary.append(term, description);
    }
    renderList("recorder-conflicts", preview.conflicts, conflict =>
      `${conflict.code}: ${conflict.path} — ${conflict.proposedResolution}`);
    renderIssues();
    renderList("recorder-timeline", preview.timeline, (item, index) =>
      `${index + 1}. ${item.actionName} (${item.actionType})${item.comment ? ` — ${item.comment}` : ""}`);
    renderMappings();
    evidenceIndex = 0;
    void showEvidence(0);
  }

  function renderIssues() {
    const list = document.getElementById("recorder-issues");
    list.replaceChildren();
    for (const issue of preview.issues) {
      const item = document.createElement("li");
      const label = document.createElement("label");
      label.className = "recorder-check";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.dataset.issueId = issue.id;
      checkbox.checked = issue.resolved;
      checkbox.disabled = issue.resolved;
      checkbox.addEventListener("change", () => { applyButton.disabled = true; });
      label.append(checkbox, document.createTextNode(
        `${issue.severity}: ${issue.title}${issue.omittedFromFlow ? " (passo omitido)" : ""}`));
      item.append(label);
      list.append(item);
    }
  }

  function renderMappings() {
    const container = document.getElementById("recorder-mapping-fields");
    container.replaceChildren();
    addMappingGroup(container, "Inputs gravados", "input", preview.recordedInputPaths,
      source => `input.${tail(source)}`);
    addMappingGroup(container, "Segredos", "secret", preview.secretReferences,
      source => `config.${tail(source)}`);
    addMappingGroup(container, "Attachments", "attachment", preview.attachmentReferences,
      source => `attachments.${tail(source)}`);
  }

  async function showEvidence(index) {
    const container = document.getElementById("recorder-evidence");
    container.replaceChildren();
    if (evidenceUrl) URL.revokeObjectURL(evidenceUrl);
    evidenceUrl = null;
    const items = preview?.evidence ?? [];
    const hasMany = items.length > 1;
    document.getElementById("previous-recorder-evidence").disabled = !hasMany;
    document.getElementById("next-recorder-evidence").disabled = !hasMany;
    if (!items.length) {
      const empty = document.createElement("p");
      empty.textContent = "Nenhuma evidência no bundle.";
      container.append(empty);
      return;
    }
    evidenceIndex = (index + items.length) % items.length;
    const item = items[evidenceIndex];
    const blob = await recorderEvidence(staging.id, staging.token, item.id, true);
    evidenceUrl = URL.createObjectURL(blob);
    const image = document.createElement("img");
    image.src = evidenceUrl;
    image.alt = `Evidência ${evidenceIndex + 1} da ação ${item.actionId}`;
    container.append(image);
  }

  async function validate() {
    try {
      if (!staging) throw new Error("Inspecione um bundle primeiro.");
      setStep("confirm");
      const result = await validateRecorderImport(staging.id, staging.token, decision());
      validationPreview.textContent = JSON.stringify(result, null, 2);
      applyButton.disabled = !result.canApply;
      setStatus(result.canApply
        ? "Decisão válida. Confira o preview e confirme o apply."
        : result.errors.join("\n"), !result.canApply);
    } catch (error) {
      applyButton.disabled = true;
      setStatus(error.message, true);
    }
  }

  async function apply() {
    try {
      setStep("apply");
      applyButton.disabled = true;
      setStatus("Publicando a nova revisão atomicamente…");
      const result = await applyRecorderImport(staging.id, staging.token, decision());
      onApplied(result);
      validationPreview.textContent = JSON.stringify({
        revisão: result.revision,
        remapeamentos: result.idRemappings,
        evidências: result.evidenceArchive,
        repetiçãoIdempotente: result.idempotentReplay
      }, null, 2);
      setStatus(`Revisão ${short(result.revision)} publicada e reaberta com sucesso.`);
      onMessage("Bundle Recorder aplicado; blocos e localizadores foram recarregados.");
    } catch (error) {
      applyButton.disabled = false;
      setStatus(error.message, true);
    }
  }

  function decision() {
    const maps = { inputMappings: {}, secretMappings: {}, attachmentMappings: {} };
    for (const input of document.querySelectorAll("[data-recorder-mapping-kind]")) {
      const target = input.value.trim();
      const collection = `${input.dataset.recorderMappingKind}Mappings`;
      maps[collection][input.dataset.recorderMappingSource] = target;
    }
    return {
      expectedRevision: preview.targetRevision,
      mode: document.getElementById("recorder-mode").value,
      subflowName: document.getElementById("recorder-subflow-name").value.trim() || null,
      remapConflicts: document.getElementById("recorder-remap-conflicts").checked,
      ...maps,
      resolvedIssueIds: [...document.querySelectorAll("[data-issue-id]:checked")]
        .map(input => input.dataset.issueId)
    };
  }

  async function close() {
    if (staging) await deleteRecorderImport(staging.id, staging.token).catch(() => undefined);
    reset(true);
    dialog.close();
  }

  function reset(removeFile) {
    if (evidenceUrl) URL.revokeObjectURL(evidenceUrl);
    evidenceUrl = null;
    staging = null;
    preview = null;
    review.hidden = true;
    mappings.hidden = true;
    confirmation.hidden = true;
    applyButton.disabled = true;
    validationPreview.textContent = "";
    if (removeFile) document.getElementById("recorder-file").value = "";
    setStep("select");
    setStatus("");
  }

  function setStatus(message, error = false) {
    status.textContent = message;
    status.classList.toggle("error", error);
  }
}

function addMappingGroup(container, title, kind, paths, defaultTarget) {
  if (!paths.length) return;
  const heading = document.createElement("h4");
  heading.textContent = title;
  container.append(heading);
  for (const source of paths) {
    const label = document.createElement("label");
    label.className = "mapping-row";
    const code = document.createElement("code");
    code.textContent = source;
    const input = document.createElement("input");
    input.value = defaultTarget(source);
    input.dataset.recorderMappingKind = kind;
    input.dataset.recorderMappingSource = source;
    label.append(code, input);
    container.append(label);
  }
}

function renderList(id, values, format) {
  const list = document.getElementById(id);
  list.replaceChildren();
  if (!values.length) {
    const item = document.createElement("li");
    item.textContent = "Nenhum.";
    list.append(item);
    return;
  }
  values.forEach((value, index) => {
    const item = document.createElement("li");
    item.textContent = format(value, index);
    list.append(item);
  });
}

function setStep(name) {
  const order = ["select", "review", "map", "confirm", "apply"];
  const current = order.indexOf(name);
  for (const item of document.querySelectorAll("[data-recorder-step]")) {
    item.classList.toggle("active", order.indexOf(item.dataset.recorderStep) <= current);
  }
}

function tail(path) {
  return path.replace(/^(?:input|secret|attachments)\.recorded\./, "");
}

function short(revision) {
  return String(revision ?? "").slice(0, 12);
}
