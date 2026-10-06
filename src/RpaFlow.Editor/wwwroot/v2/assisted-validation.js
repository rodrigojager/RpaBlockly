import {
  assistedEvidence,
  assistedHumanHandoffEvidence,
  getAssistedExecution,
  getLatestAssistedExecution,
  readConfiguration,
  respondAssistedHumanHandoff,
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
  const humanize = document.getElementById("assisted-humanize");
  const mouseAlgorithm = document.getElementById("assisted-mouse-algorithm");
  const compatibilityMode = document.getElementById("assisted-compatibility-mode");
  const boundary = document.getElementById("assisted-boundary");
  const screenshots = document.getElementById("assisted-capture-screenshots");
  const confirmation = document.getElementById("assisted-confirm-boundary");
  const startButton = document.getElementById("start-assisted-validation");
  const stopButton = document.getElementById("stop-assisted-validation");
  const timeline = document.getElementById("assisted-timeline");
  const gallery = document.getElementById("assisted-evidence-gallery");
  const progressCount = document.getElementById("assisted-progress-count");
  const evidenceCount = document.getElementById("assisted-evidence-count");
  const handoffList = document.getElementById("assisted-handoff-list");
  const handoffCount = document.getElementById("assisted-handoff-count");
  const actionCards = new Map();
  const evidenceCards = new Map();
  const handoffCards = new Map();
  const handoffEvidenceUrls = new Map();
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
  browser.addEventListener("change", updateSpyBrowserControls);
  humanize.addEventListener("change", updateSpyBrowserControls);
  mouseAlgorithm.addEventListener("change", updateSpyBrowserControls);
  compatibilityMode.addEventListener("change", updateSpyBrowserControls);
  for (const button of dialog.querySelectorAll(".help-tip")) {
    button.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      button.classList.toggle("is-open");
    });
  }
  updateSpyBrowserControls();

  function updateSpyBrowserControls() {
    const visible = browser.value === "spybrowser";
    for (const control of dialog.querySelectorAll(".assisted-spybrowser-only")) {
      control.hidden = !visible;
    }
    document.getElementById("assisted-algorithm-label").hidden = !visible || !humanize.checked;
    document.getElementById("assisted-compatibility-label").hidden = !visible || !humanize.checked;
    mouseAlgorithm.disabled = !visible || !humanize.checked;
    compatibilityMode.disabled = !visible || !humanize.checked;
  }

  async function open() {
    renderBoundaries();
    dialog.showModal();
    try {
      const config = await readConfiguration();
      const runtime = config?.Runtime ?? config?.runtime ?? {};
      browser.value = ["spybrowser", "chromium", "cloakbrowser"].includes(String(runtime.Browser ?? runtime.browser ?? "spybrowser").toLowerCase())
        ? String(runtime.Browser ?? runtime.browser ?? "spybrowser").toLowerCase() : "spybrowser";
      humanize.checked = (runtime.SpyBrowserHumanize ?? runtime.spyBrowserHumanize ?? true) === true;
      mouseAlgorithm.value = String(runtime.SpyBrowserMouseAlgorithm ?? runtime.spyBrowserMouseAlgorithm ?? "bezier").toLowerCase();
      compatibilityMode.value = String(runtime.SpyBrowserCompatibilityMode ?? runtime.spyBrowserCompatibilityMode ?? "legacy").toLowerCase();
      updateSpyBrowserControls();
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
    const actions = executableActions(documents().flow);
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
      const request = {
        expectedRevision: revision(),
        flow: current.flow,
        locators: current.locators,
        policy: current.policy,
        browser: browser.value,
        boundaryActionId: boundary.value,
        captureScreenshots: screenshots.checked
      };
      if (browser.value === "spybrowser") {
        request.spyBrowserHumanize = humanize.checked;
        if (humanize.checked) {
          request.spyBrowserMouseAlgorithm = mouseAlgorithm.value;
          request.spyBrowserCompatibilityMode = compatibilityMode.value;
        }
      }
      const result = await startAssistedExecution(request);
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
    for (const handoff of snapshot.humanHandoffs ?? []) renderHandoff(handoff);
    progressCount.textContent = `${snapshot.executedActions ?? 0} ` +
      `${snapshot.executedActions === 1 ? "etapa" : "etapas"}`;
    evidenceCount.textContent = `${evidenceCards.size} ` +
      `${evidenceCards.size === 1 ? "captura" : "capturas"}`;
    const pendingHandoffs = (snapshot.humanHandoffs ?? [])
      .filter(item => item.state?.toLowerCase() === "pending");
    handoffCount.textContent = `${pendingHandoffs.length} ` +
      `${pendingHandoffs.length === 1 ? "pendente" : "pendentes"}`;
    setRunning(snapshot.canStop === true);
    const status = statusText(snapshot);
    setStatus(snapshot.status, status.title, status.detail);
    if (pendingHandoffs.length > 0) {
      setStatus(
        "running",
        "Intervenção humana necessária",
        "Resolva o desafio no navegador da execução e confirme a retomada abaixo.");
    }
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

  function renderHandoff(handoff) {
    let card = handoffCards.get(handoff.requestId);
    if (!card) {
      if (handoffList.querySelector(".assisted-empty")) handoffList.replaceChildren();
      card = document.createElement("article");
      card.className = "assisted-handoff-card";

      const preview = document.createElement("div");
      preview.className = "assisted-handoff-preview";
      preview.textContent = handoff.evidenceAvailable
        ? "Carregando captura…"
        : "Captura indisponível";

      const body = document.createElement("div");
      body.className = "assisted-handoff-body";
      const heading = document.createElement("div");
      heading.className = "assisted-handoff-heading";
      const name = document.createElement("strong");
      name.textContent = `${handoff.provider ?? "captcha"} · ${handoff.kind}`;
      const status = document.createElement("span");
      status.dataset.handoffStatus = "";
      heading.append(name, status);
      const message = document.createElement("p");
      message.textContent = handoff.message;
      const meta = document.createElement("small");
      meta.textContent = `Ação ${handoff.actionId} · expira ${formatDate(handoff.expiresAtUtc)}`;
      const actions = document.createElement("div");
      actions.className = "assisted-handoff-actions";
      const continueButton = document.createElement("button");
      continueButton.type = "button";
      continueButton.dataset.handoffAction = "continue";
      continueButton.textContent = "Confirmar e retomar";
      continueButton.addEventListener("click", () => {
        void respondToHandoff(handoff.requestId, "continue");
      });
      const rejectButton = document.createElement("button");
      rejectButton.type = "button";
      rejectButton.className = "danger";
      rejectButton.dataset.handoffAction = "reject";
      rejectButton.textContent = "Rejeitar retomada";
      rejectButton.addEventListener("click", () => {
        void respondToHandoff(handoff.requestId, "reject");
      });
      actions.append(continueButton, rejectButton);
      body.append(heading, message, meta, actions);
      card.append(preview, body);
      handoffList.prepend(card);
      handoffCards.set(handoff.requestId, card);
      if (handoff.evidenceAvailable) void renderHandoffEvidence(handoff, preview);
    }

    const stateName = handoff.state?.toLowerCase() ?? "pending";
    card.dataset.state = stateName;
    card.querySelector("[data-handoff-status]").textContent = handoffStateLabel(stateName);
    const pending = stateName === "pending";
    for (const button of card.querySelectorAll("[data-handoff-action]")) {
      button.disabled = !pending;
    }
  }

  async function renderHandoffEvidence(handoff, preview) {
    if (!executionId || handoffEvidenceUrls.has(handoff.requestId)) return;
    try {
      const blob = await assistedHumanHandoffEvidence(executionId, handoff.requestId);
      const objectUrl = URL.createObjectURL(blob);
      handoffEvidenceUrls.set(handoff.requestId, objectUrl);
      const image = document.createElement("img");
      image.src = objectUrl;
      image.alt = `Captura para intervenção em ${handoff.kind}`;
      image.addEventListener("click", () => window.open(objectUrl, "_blank", "noopener"));
      preview.replaceWith(image);
    } catch {
      preview.textContent = "A captura não está mais disponível.";
    }
  }

  async function respondToHandoff(requestId, action) {
    if (!executionId) return;
    const card = handoffCards.get(requestId);
    for (const button of card?.querySelectorAll("button") ?? []) button.disabled = true;
    try {
      const snapshot = await respondAssistedHumanHandoff(executionId, requestId, action);
      renderSnapshot(snapshot);
      schedulePoll(0);
      onMessage(action === "continue"
        ? "Retomada confirmada para a execução assistida."
        : "Retomada rejeitada; a execução encerrará com falha.");
    } catch (error) {
      onMessage(error.message, true);
      schedulePoll(0);
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
    for (const objectUrl of handoffEvidenceUrls.values()) URL.revokeObjectURL(objectUrl);
    handoffEvidenceUrls.clear();
    handoffCards.clear();
    timeline.innerHTML = '<li class="assisted-empty">Preparando a primeira etapa…</li>';
    gallery.innerHTML = '<p class="assisted-empty">Aguardando a primeira captura…</p>';
    handoffList.innerHTML = '<p class="assisted-empty">Nenhuma intervenção solicitada.</p>';
    progressCount.textContent = "0 etapas";
    evidenceCount.textContent = "0 capturas";
    handoffCount.textContent = "0 pendentes";
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

function handoffStateLabel(state) {
  switch (state) {
    case "pending": return "Aguardando operador";
    case "continuing": return "Retomando";
    case "rejecting": return "Rejeitando";
    case "acknowledged": return "Retomada confirmada";
    case "rejected": return "Rejeitada";
    case "expired": return "Expirada";
    case "cancelled": return "Cancelada";
    default: return state;
  }
}

function formatDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "em horário desconhecido" : date.toLocaleString("pt-BR");
}

function executableActions(flow) {
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
  switch (value) {
    case "spybrowser": return "SpyBrowser";
    case "cloakbrowser": return "CloakBrowser";
    default: return "Chromium Playwright";
  }
}
