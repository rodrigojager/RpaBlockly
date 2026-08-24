import {
  assistedEvidence,
  getAssistedExecution,
  getLatestAssistedExecution,
  startAssistedExecution,
  stopAssistedExecution
} from "./api.js";

const terminalStatuses = new Set(["validated", "cancelled", "failed"]);

export function initializeAssistedValidation({
  documents,
  revision,
  onAction,
  onMessage
}) {
  const dialog = document.getElementById("assisted-validation-dialog");
  const state = document.getElementById("assisted-runtime-state");
  const title = document.getElementById("assisted-status-title");
  const detail = document.getElementById("assisted-status-detail");
  const browser = document.getElementById("assisted-browser");
  const boundary = document.getElementById("assisted-boundary");
  const screenshots = document.getElementById("assisted-capture-screenshots");
  const confirmation = document.getElementById("assisted-confirm-boundary");
  const startButton = document.getElementById("start-assisted-validation");
  const stopButton = document.getElementById("stop-assisted-validation");
  const timeline = document.getElementById("assisted-timeline");
  const gallery = document.getElementById("assisted-evidence-gallery");
  const progressCount = document.getElementById("assisted-progress-count");
  const evidenceCount = document.getElementById("assisted-evidence-count");
  const actionCards = new Map();
  const evidenceCards = new Map();
  let executionId = null;
  let afterSequence = 0;
  let pollTimer = null;
  let polling = false;

  document.getElementById("open-assisted-validation").addEventListener("click", open);
  document.getElementById("close-assisted-validation").addEventListener("click", () => {
    dialog.close();
  });
  startButton.addEventListener("click", start);
  stopButton.addEventListener("click", stop);

  async function open() {
    renderBoundaries();
    dialog.showModal();
    try {
      const result = executionId
        ? await getAssistedExecution(executionId, afterSequence)
        : await getLatestAssistedExecution();
      if (!result) return;
      executionId = result.executionId;
      renderSnapshot(result);
      if (!terminalStatuses.has(result.status)) schedulePoll(0);
    } catch (error) {
      onMessage(error.message, true);
    }
  }

  function renderBoundaries() {
    const selected = boundary.value;
    boundary.replaceChildren();
    const actions = leafActions(documents().flow);
    for (const action of actions) {
      const option = document.createElement("option");
      option.value = action.id;
      option.textContent = `${action.position}. ${action.name} — ${action.type}`;
      boundary.append(option);
    }
    if (actions.some(action => action.id === selected)) boundary.value = selected;
    if (actions.length === 0) {
      const option = document.createElement("option");
      option.textContent = "O fluxo não possui uma ação-folha executável";
      option.value = "";
      boundary.append(option);
    }
  }

  async function start() {
    try {
      if (!confirmation.checked) {
        throw new Error("Confirme explicitamente a última etapa segura antes de iniciar.");
      }
      if (!boundary.value) throw new Error("Escolha a última etapa segura permitida.");
      const current = documents();
      resetResults();
      setStatus("starting", "Preparando o navegador", "Validando o snapshot do rascunho atual.");
      setRunning(true);
      const result = await startAssistedExecution({
        expectedRevision: revision(),
        flow: current.flow,
        locators: current.locators,
        policy: current.policy,
        browser: browser.value,
        boundaryActionId: boundary.value,
        captureScreenshots: screenshots.checked
      });
      executionId = result.executionId;
      renderSnapshot(result);
      schedulePoll(0);
      onMessage("Homologação assistida iniciada em um navegador separado.");
    } catch (error) {
      setRunning(false);
      setStatus("failed", "Não foi possível iniciar", error.message);
      onMessage(error.message, true);
    }
  }

  async function stop() {
    if (!executionId) return;
    try {
      const result = await stopAssistedExecution(executionId);
      renderSnapshot(result);
      schedulePoll(0);
    } catch (error) {
      onMessage(error.message, true);
    }
  }

  function schedulePoll(delay = 450) {
    window.clearTimeout(pollTimer);
    if (!executionId || polling) return;
    pollTimer = window.setTimeout(poll, delay);
  }

  async function poll() {
    if (!executionId || polling) return;
    pollTimer = null;
    polling = true;
    try {
      const result = await getAssistedExecution(executionId, afterSequence);
      renderSnapshot(result);
    } catch (error) {
      setStatus("failed", "Conexão com a execução interrompida", error.message);
      setRunning(false);
    } finally {
      polling = false;
      if (executionId && !terminalStatuses.has(state.dataset.status)) {
        schedulePoll();
      }
    }
  }

  function renderSnapshot(snapshot) {
    for (const event of snapshot.events ?? []) {
      afterSequence = Math.max(afterSequence, event.sequence ?? 0);
      renderEvent(event);
    }
    for (const evidence of snapshot.evidence ?? []) renderEvidence(evidence);
    progressCount.textContent = `${snapshot.executedActions ?? 0} ` +
      `${snapshot.executedActions === 1 ? "etapa" : "etapas"}`;
    evidenceCount.textContent = `${evidenceCards.size} ` +
      `${evidenceCards.size === 1 ? "captura" : "capturas"}`;
    setRunning(snapshot.canStop === true);
    const status = statusText(snapshot);
    setStatus(snapshot.status, status.title, status.detail);
    if (terminalStatuses.has(snapshot.status)) {
      window.clearTimeout(pollTimer);
      pollTimer = null;
      confirmation.checked = false;
      if (snapshot.status === "validated") {
        onMessage(`Roteiro validado até “${snapshot.boundaryActionName}”.`);
      }
    }
  }

  function renderEvent(event) {
    if (event.kind === "actionStarted") {
      const card = ensureActionCard(event);
      card.dataset.status = "running";
      card.querySelector("[data-step-status]").textContent = "Executando";
      onAction(event.actionId, "running");
      return;
    }
    if (event.kind === "actionCompleted") {
      const card = ensureActionCard(event);
      card.dataset.status = "completed";
      card.querySelector("[data-step-status]").textContent =
        event.elapsedMilliseconds === null || event.elapsedMilliseconds === undefined
          ? "Concluída"
          : `Concluída em ${event.elapsedMilliseconds} ms`;
      return;
    }
    if (event.kind === "actionFailed") {
      const card = ensureActionCard(event);
      card.dataset.status = "failed";
      card.querySelector("[data-step-status]").textContent =
        `Falhou${event.failureCategory ? ` · ${event.failureCategory}` : ""}`;
      onAction(event.actionId, "failed");
      return;
    }
    if (event.kind === "actionEvidenceCaptured" && event.evidenceId) {
      const card = ensureActionCard(event);
      card.querySelector("[data-step-evidence]").textContent = "Captura salva";
      return;
    }
    if (event.kind === "actionEvidenceFailed") {
      const card = ensureActionCard(event);
      card.querySelector("[data-step-evidence]").textContent =
        "Captura indisponível; a etapa continuou";
    }
  }

  function ensureActionCard(event) {
    const key = event.actionId ?? `evento-${event.sequence}`;
    if (actionCards.has(key)) return actionCards.get(key);
    if (timeline.querySelector(".assisted-empty")) timeline.replaceChildren();
    const item = document.createElement("li");
    item.className = "assisted-step-card";
    item.dataset.status = "pending";
    const marker = document.createElement("span");
    marker.className = "assisted-step-marker";
    marker.setAttribute("aria-hidden", "true");
    const body = document.createElement("div");
    const heading = document.createElement("strong");
    heading.textContent = event.actionName ?? event.actionId ?? "Etapa";
    const meta = document.createElement("p");
    meta.textContent = `${event.actionType ?? "ação"} · ${event.actionId ?? "sem ID"}`;
    const statusLine = document.createElement("span");
    statusLine.dataset.stepStatus = "";
    statusLine.textContent = "Preparando";
    const evidenceLine = document.createElement("small");
    evidenceLine.dataset.stepEvidence = "";
    body.append(heading, meta, statusLine, evidenceLine);
    item.append(marker, body);
    timeline.append(item);
    actionCards.set(key, item);
    return item;
  }

  async function renderEvidence(evidence) {
    if (evidenceCards.has(evidence.id) || !executionId) return;
    if (gallery.querySelector(".assisted-empty")) gallery.replaceChildren();
    const figure = document.createElement("figure");
    figure.className = "assisted-evidence-card";
    figure.dataset.kind = evidence.kind;
    const placeholder = document.createElement("div");
    placeholder.className = "assisted-evidence-loading";
    placeholder.textContent = "Carregando captura…";
    const caption = document.createElement("figcaption");
    caption.textContent = evidence.actionName ??
      (evidence.kind === "failure" ? "Falha da execução" : evidence.fileName);
    figure.append(placeholder, caption);
    gallery.prepend(figure);
    evidenceCards.set(evidence.id, figure);
    try {
      const blob = await assistedEvidence(executionId, evidence.id);
      const image = document.createElement("img");
      const objectUrl = URL.createObjectURL(blob);
      image.src = objectUrl;
      image.alt = `Captura: ${caption.textContent}`;
      image.addEventListener("load", () => placeholder.replaceWith(image), { once: true });
      image.addEventListener("error", () => {
        URL.revokeObjectURL(objectUrl);
        placeholder.textContent = "Não foi possível exibir a captura.";
      }, { once: true });
      image.addEventListener("click", () => window.open(objectUrl, "_blank", "noopener"));
    } catch {
      placeholder.textContent = "A captura não está mais disponível.";
    }
  }

  function resetResults() {
    afterSequence = 0;
    actionCards.clear();
    for (const card of evidenceCards.values()) {
      const image = card.querySelector("img");
      if (image?.src.startsWith("blob:")) URL.revokeObjectURL(image.src);
    }
    evidenceCards.clear();
    timeline.innerHTML = '<li class="assisted-empty">Preparando a primeira etapa…</li>';
    gallery.innerHTML = '<p class="assisted-empty">Aguardando a primeira captura…</p>';
    progressCount.textContent = "0 etapas";
    evidenceCount.textContent = "0 capturas";
  }

  function setRunning(running) {
    startButton.disabled = running;
    stopButton.disabled = !running;
    browser.disabled = running;
    boundary.disabled = running;
    screenshots.disabled = running;
    confirmation.disabled = running;
  }

  function setStatus(status, heading, message) {
    state.dataset.status = status;
    title.textContent = heading;
    detail.textContent = message;
  }

  return { open, renderBoundaries };
}

function leafActions(flow) {
  const result = [];
  let position = 0;
  const visit = (actions, prefix) => {
    for (const action of actions ?? []) {
      position += 1;
      const current = `${prefix}${position}`;
      const nested = [...(action.actions ?? []), ...(action.elseActions ?? [])];
      if (nested.length === 0) result.push({ ...action, position: current });
      else {
        if (action.type === "runSubflow") result.push({ ...action, position: current });
        visit(action.actions, `${current}.`);
        visit(action.elseActions, `${current}.`);
      }
    }
  };
  visit(flow.actions, "");
  for (const [name, actions] of Object.entries(flow.subflows ?? {})) {
    visit(actions, `${name}.`);
  }
  return result;
}

function statusText(snapshot) {
  switch (snapshot.status) {
    case "starting":
      return { title: "Preparando o navegador", detail: "O snapshot está sendo validado." };
    case "running":
      return {
        title: "Validando roteiro",
        detail: `Navegador ${browserName(snapshot.browser)} em execução. Não feche a janela.`
      };
    case "stopping":
      return { title: "Interrompendo com segurança", detail: "Aguardando a ação atual liberar o navegador." };
    case "validated":
      return {
        title: "Roteiro validado até o limite seguro",
        detail: `A execução parou depois de “${snapshot.boundaryActionName}”.`
      };
    case "cancelled":
      return { title: "Homologação interrompida", detail: "O navegador foi fechado sem executar novas etapas." };
    case "failed":
      return { title: "A homologação encontrou uma falha", detail: snapshot.error ?? "Revise a última etapa exibida." };
    default:
      return { title: "Pronto para configurar", detail: "Nenhuma homologação foi iniciada." };
  }
}

function browserName(value) {
  return value === "cloakbrowser" ? "CloakBrowser" : "Chromium Playwright";
}
