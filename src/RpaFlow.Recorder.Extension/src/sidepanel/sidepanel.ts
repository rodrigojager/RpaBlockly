import { buildBundle, verifyBundleIntegrity, type BundleComment } from "../bundle/bundle.js";
import { recorderCheckpointKey } from "../core/checkpoint-store.js";
import { stableId, slug } from "../core/stable.js";
import type {
  NormalizedIntent,
  RawCaptureEvent,
  RecorderCheckpoint,
  RecorderOptions
} from "../core/types.js";
import { EvidenceStore, slideshowItems, type EvidenceAsset } from "../evidence/evidence.js";
import { assertFinalizable, generatePackage } from "../package/generator.js";
import { validateGeneratedPackage } from "../package/validator.js";
import {
  generateRecipientAccess,
  generateSharingPassword,
  type GeneratedRecipientAccess
} from "../security/recovery.js";
import { EncryptedSecretStore } from "../security/secret-store.js";
import {
  isRecorderUiRefresh,
  type RecorderAccessNotice,
  type RecorderRequest,
  type RecorderResponse,
  type RecorderTarget
} from "../shared/messages.js";
import { hydrateUploads, UploadStore } from "../uploads/upload-store.js";

const evidenceStore = new EvidenceStore();
const secretStore = new EncryptedSecretStore();
const uploadStore = new UploadStore();
const slideshowObjectUrls: string[] = [];
const timelineObjectUrls: string[] = [];
const downloadObjectUrls: string[] = [];
const continuousHostOrigins = ["<all_urls>"];
let evidence: EvidenceAsset[] = [];
let evidenceIndex = 0;
let exportCancelled = false;
let activeDownloadId: number | undefined;
let comments: BundleComment[] = [];
let generatedRecipientAccess: GeneratedRecipientAccess | undefined;
let currentCheckpoint: RecorderCheckpoint | undefined;
let currentTarget: RecorderTarget | undefined;
let targetChecking = true;
let startInProgress = false;
let renderRevision = 0;
let pendingCheckpoint: RecorderCheckpoint | undefined;
let checkpointRenderTimer: ReturnType<typeof setTimeout> | undefined;

const status = element<HTMLParagraphElement>("status");
const recordingIndicator = element<HTMLDivElement>("recording-indicator");
const recordingSummary = element<HTMLParagraphElement>("recording-summary");
const evidenceCaptureStatus = element<HTMLParagraphElement>("evidence-capture-status");
const issueList = element<HTMLOListElement>("issues");
const timeline = element<HTMLOListElement>("timeline");
const timelineEmpty = element<HTMLParagraphElement>("timeline-empty");
const issueCount = element<HTMLSpanElement>("issue-count");
const stepCount = element<HTMLSpanElement>("step-count");
const pageTarget = element<HTMLDivElement>("page-target");
const pageTargetIcon = element<HTMLSpanElement>("page-target-icon");
const pageTargetTitle = element<HTMLElement>("page-target-title");
const pageTargetDetail = element<HTMLElement>("page-target-detail");
const restoreAccessButton = element<HTMLButtonElement>("restore-access");
const secretToggle = element<HTMLInputElement>("capture-secrets");
const secretOptions = element<HTMLDivElement>("secret-options");
const simpleSecretOptions = element<HTMLDivElement>("simple-secret-options");
const advancedSecretOptions = element<HTMLDivElement>("advanced-secret-options");
const sharingPassword = element<HTMLInputElement>("secret-sharing-password");
const recoveryOutput = element<HTMLDivElement>("recovery-output");
const recoveryKey = element<HTMLTextAreaElement>("recovery-key");
const exportSection = element<HTMLElement>("export-section");
const exportProgress = element<HTMLProgressElement>("export-progress");
const exportMessage = element<HTMLParagraphElement>("export-message");

element<HTMLButtonElement>("start").addEventListener("click", () => void start().catch(showError));
restoreAccessButton.addEventListener("click", () => void restoreBroadAccess().catch(showError));
element<HTMLButtonElement>("pause").addEventListener("click", () => void invokeAndRender({ type: "RECORDER_PAUSE" }));
element<HTMLButtonElement>("resume").addEventListener("click", () => void invokeAndRender({ type: "RECORDER_RESUME" }));
element<HTMLButtonElement>("cancel").addEventListener("click", () => void cancel());
element<HTMLButtonElement>("finalize").addEventListener("click", () => void finalizeAndDownload());
element<HTMLButtonElement>("cancel-export").addEventListener("click", () => {
  exportCancelled = true;
  if (activeDownloadId !== undefined) void chrome.downloads.cancel(activeDownloadId);
});
element<HTMLButtonElement>("previous-evidence").addEventListener("click", () => showEvidence(evidenceIndex - 1));
element<HTMLButtonElement>("next-evidence").addEventListener("click", () => showEvidence(evidenceIndex + 1));
element<HTMLButtonElement>("remove-evidence").addEventListener("click", () => void removeCurrentEvidence());
element<HTMLInputElement>("secret-mode-simple").addEventListener("change", syncSecretMode);
element<HTMLInputElement>("secret-mode-advanced").addEventListener("change", syncSecretMode);
element<HTMLButtonElement>("generate-password").addEventListener("click", generatePassword);
element<HTMLButtonElement>("toggle-password").addEventListener("click", togglePasswordVisibility);
element<HTMLButtonElement>("copy-password").addEventListener("click", () => void copyPassword());
element<HTMLButtonElement>("generate-recovery-key").addEventListener("click", () =>
  void prepareSimpleRecipientAccess().catch(showError));
element<HTMLButtonElement>("copy-recovery-key").addEventListener("click", () => void copyRecoveryKey());
sharingPassword.addEventListener("input", invalidateGeneratedRecipientAccess);
secretToggle.addEventListener("change", syncSecretOptions);
issueList.addEventListener("click", (event) => void resolveIssue(event));
timeline.addEventListener("change", (event) => void updateComment(event));
addEventListener("unload", revokeObjectUrls);
chrome.storage.onChanged.addListener((changes, areaName) => {
  const change = changes[recorderCheckpointKey];
  if (areaName !== "session" || change?.newValue === undefined) return;
  scheduleCheckpointRender(change.newValue as RecorderCheckpoint);
});
chrome.runtime.onMessage.addListener((message: unknown, sender) => {
  if (sender.id !== chrome.runtime.id || !isRecorderUiRefresh(message)) return false;
  void refreshRecorderUi().catch(showError);
  return false;
});
chrome.tabs.onActivated.addListener(() => void refreshActiveTarget());
chrome.tabs.onUpdated.addListener((_tabId, changeInfo, tab) => {
  if (tab.active && (changeInfo.url !== undefined || changeInfo.status === "complete")) {
    void refreshActiveTarget();
  }
});

void initialize();

async function initialize(): Promise<void> {
  syncSecretOptions();
  comments = await loadComments();
  const response = await send({ type: "RECORDER_GET_STATE" });
  await render(response.checkpoint);
  await refreshActiveTarget();
}

async function start(): Promise<void> {
  if (!element<HTMLInputElement>("privacy-accepted").checked) {
    setStatus("Confirme o aviso de privacidade antes de iniciar.", true);
    return;
  }
  startInProgress = true;
  syncControls();
  const startButton = element<HTMLButtonElement>("start");
  startButton.textContent = "Iniciando…";
  startButton.setAttribute("aria-busy", "true");
  setStatus("Solicitando acesso contínuo às páginas HTTP(S)…");
  try {
    const granted = await chrome.permissions.request({ origins: continuousHostOrigins });
    if (!granted) {
      setStatus(
        "O Recorder precisa do acesso amplo para acompanhar mudanças de site sem interromper a gravação.",
        true
      );
      return;
    }
    await refreshActiveTarget();
    const target = currentTarget;
    if (target === undefined) {
      setStatus("Abra uma página HTTP(S) em uma aba normal e tente iniciar novamente.", true);
      return;
    }
    setStatus("Ativando a gravação contínua na página escolhida…");
    const captureScreenshots = element<HTMLInputElement>("capture-screenshots").checked;
    const captureSecrets = secretToggle.checked;
    let recipientOptions: Pick<RecorderOptions, "recipientKeyId" | "recipientPublicKeyPem"> = {};
    if (captureSecrets && element<HTMLInputElement>("secret-mode-simple").checked) {
      if (generatedRecipientAccess === undefined) {
        await prepareSimpleRecipientAccess();
        setStatus("Chave gerada. Copie a senha e a chave de recuperação e confirme antes de iniciar.", true);
        return;
      }
      if (!element<HTMLInputElement>("recovery-copied").checked) {
        setStatus("Confirme que copiou a senha e a chave de recuperação antes de iniciar.", true);
        return;
      }
      recipientOptions = {
        recipientKeyId: generatedRecipientAccess.keyId,
        recipientPublicKeyPem: generatedRecipientAccess.publicKeyPem
      };
    } else if (captureSecrets) {
      recipientOptions = {
        recipientKeyId: element<HTMLInputElement>("recipient-key-id").value.trim(),
        recipientPublicKeyPem: element<HTMLTextAreaElement>("recipient-public-key").value.trim()
      };
    }
    const options: RecorderOptions = {
      captureScreenshots,
      captureSecrets,
      includeUploads: element<HTMLInputElement>("include-uploads").checked,
      ...recipientOptions
    };
    const response = await send({
      type: "RECORDER_START",
      name: element<HTMLInputElement>("session-name").value.trim() || "Nova gravação",
      tabId: target.tabId,
      origin: target.origin,
      options
    });
    await render(response.checkpoint);
  } finally {
    startInProgress = false;
    startButton.removeAttribute("aria-busy");
    syncControls();
  }
}

async function restoreBroadAccess(): Promise<void> {
  restoreAccessButton.disabled = true;
  setStatus("Solicitando novamente o acesso contínuo às páginas HTTP(S)…");
  try {
    const granted = await chrome.permissions.request({ origins: continuousHostOrigins });
    if (!granted) {
      setStatus("O acesso não foi concedido; a sessão continua pausada para não perder ações.", true);
      return;
    }
    const response = await send({ type: "RECORDER_RECONNECT" });
    await render(response.checkpoint);
    await refreshActiveTarget();
    setStatus("Acesso restaurado. Clique em Retomar quando estiver pronto para continuar.");
  } finally {
    restoreAccessButton.disabled = false;
  }
}

async function finalizeAndDownload(): Promise<void> {
  exportCancelled = false;
  exportSection.hidden = false;
  setProgress(5, "Validando a gravação…");
  let transitioned = false;
  try {
    const response = await send({ type: "RECORDER_FINALIZE" });
    if (response.checkpoint === undefined) throw new Error("Sessão ausente.");
    transitioned = true;
    const checkpoint = hydrateUploads(response.checkpoint, await uploadStore.list());
    const generated = generatePackage(checkpoint.name, checkpoint.events, checkpoint.resolvedIssueIds);
    assertFinalizable(generated);
    validateGeneratedPackage(generated);
    checkCancelled();
    setProgress(30, "Reunindo evidências e conteúdo consentido…");
    evidence = await evidenceStore.list();
    const secrets = await secretStore.list();
    checkCancelled();
    setProgress(55, "Calculando integridade e montando o ZIP…");
    const createdAtUtc = new Date().toISOString();
    const built = await buildBundle({
      bundleId: stableId("bundle", checkpoint.sessionId, createdAtUtc),
      createdAtUtc,
      checkpoint,
      generated,
      evidence,
      secrets,
      comments
    });
    await verifyBundleIntegrity(built.bytes);
    checkCancelled();
    setProgress(82, "Aguardando confirmação do download…");
    const url = URL.createObjectURL(new Blob(
      [built.bytes.slice().buffer as ArrayBuffer],
      { type: "application/zip" }
    ));
    downloadObjectUrls.push(url);
    const downloadId = await chrome.downloads.download({
      url,
      filename: `${slug(checkpoint.name, "gravacao")}.rpablockly.zip`,
      saveAs: true,
      conflictAction: "uniquify"
    });
    activeDownloadId = downloadId;
    await waitForDownload(downloadId);
    activeDownloadId = undefined;
    setProgress(100, "Download confirmado. A sessão local foi limpa.");
    await send({ type: "RECORDER_COMPLETE" });
    await clearComments();
    clearSensitiveAccess();
    await render(undefined);
    await refreshActiveTarget();
    setStatus("Bundle V2 baixado com sucesso.");
  } catch (error) {
    activeDownloadId = undefined;
    if (transitioned) await send({ type: "RECORDER_ABORT_FINALIZE" }).catch(() => undefined);
    setStatus(error instanceof Error ? error.message : "A exportação falhou.", true);
    setProgress(0, "Exportação interrompida; a sessão foi preservada.");
  }
}

async function cancel(): Promise<void> {
  await send({ type: "RECORDER_CANCEL" });
  await clearComments();
  clearSensitiveAccess();
  evidence = [];
  await render(undefined);
  await refreshActiveTarget();
  setStatus("Sessão excluída.");
}

async function invokeAndRender(request: RecorderRequest): Promise<void> {
  try {
    const response = await send(request);
    await render(response.checkpoint);
    await refreshActiveTarget();
  } catch (error) {
    setStatus(error instanceof Error ? error.message : "Operação indisponível.", true);
  }
}

async function render(checkpoint: RecorderCheckpoint | undefined): Promise<void> {
  const revision = ++renderRevision;
  const state = checkpoint?.state ?? "idle";
  const generated = checkpoint === undefined
    ? undefined
    : generatePackage(checkpoint.name, checkpoint.events, checkpoint.resolvedIssueIds);
  const loadedEvidence = checkpoint === undefined ? [] : await evidenceStore.list();
  if (revision !== renderRevision) return;
  currentCheckpoint = checkpoint;
  evidence = loadedEvidence;
  setStatus(checkpoint === undefined ? "Nenhuma sessão ativa." : stateLabel(state));
  syncControls();
  setConfigurationEnabled(checkpoint === undefined);
  if (checkpoint === undefined) {
    issueList.replaceChildren();
    timeline.replaceChildren();
    issueCount.textContent = "0";
    stepCount.textContent = "0";
    timelineEmpty.hidden = false;
    recordingIndicator.hidden = true;
    recordingSummary.hidden = true;
    evidenceCaptureStatus.hidden = true;
    revokeTimelineObjectUrls();
    showEvidence(0);
    return;
  }
  const packagePreview = generated!;
  updateRecordingFeedback(checkpoint, packagePreview.intents);
  renderEvidenceCaptureStatus(checkpoint, loadedEvidence.length);
  renderIssues(packagePreview.issues);
  renderTimeline(packagePreview.intents, checkpoint.events, evidence);
  showEvidence(Math.min(evidenceIndex, Math.max(0, evidence.length - 1)));
}

function renderEvidenceCaptureStatus(
  checkpoint: RecorderCheckpoint,
  storedEvidenceCount: number
): void {
  if (!checkpoint.options.captureScreenshots) {
    evidenceCaptureStatus.hidden = true;
    return;
  }
  const capture = checkpoint.evidenceCapture ?? {
    attempted: 0,
    captured: storedEvidenceCount,
    skipped: 0,
    failed: 0
  };
  evidenceCaptureStatus.hidden = false;
  evidenceCaptureStatus.dataset.state = capture.failed > 0 ? "failed" : "ready";
  const savedLabel = storedEvidenceCount === 1 ? "1 captura salva" : `${storedEvidenceCount} capturas salvas`;
  const skippedLabel = capture.skipped === 1 ? "1 evento agrupado" : `${capture.skipped} eventos agrupados`;
  evidenceCaptureStatus.textContent = capture.lastFailure === undefined
    ? `Evidências visuais: ${savedLabel}; ${skippedLabel}.`
    : `Evidências visuais: ${savedLabel}; ${capture.failed} falha(s). ${capture.lastFailure.message}`;
}

function renderIssues(issues: ReturnType<typeof generatePackage>["issues"]): void {
  issueList.replaceChildren();
  issueCount.textContent = String(issues.filter((issue) => !issue.resolved).length);
  for (const issue of issues) {
    const fragment = element<HTMLTemplateElement>("issue-template").content.cloneNode(true) as DocumentFragment;
    const item = fragment.querySelector("li")!;
    item.setAttribute("data-severity", issue.severity);
    fragment.querySelector("strong")!.textContent = issue.resolved ? `${issue.title} — resolvida` : issue.title;
    fragment.querySelector("p")!.textContent = issue.technicalDetail;
    const button = fragment.querySelector("button")!;
    button.dataset.issueId = issue.id;
    button.disabled = issue.resolved;
    issueList.append(fragment);
  }
}

function renderTimeline(
  intents: ReturnType<typeof generatePackage>["intents"],
  events: RawCaptureEvent[],
  assets: EvidenceAsset[]
): void {
  stepCount.textContent = String(intents.length);
  timelineEmpty.hidden = intents.length > 0;
  revokeTimelineObjectUrls();
  const existing = new Map(
    [...timeline.querySelectorAll<HTMLLIElement>("li[data-action-id]")]
      .map((item) => [item.dataset.actionId!, item] as const)
  );
  const retained = new Set<string>();
  intents.forEach((intent, index) => {
    let item = existing.get(intent.actionId);
    if (item === undefined) {
      const fragment = element<HTMLTemplateElement>("step-template").content.cloneNode(true) as DocumentFragment;
      item = fragment.querySelector<HTMLLIElement>("li")!;
    }
    retained.add(intent.actionId);
    item.dataset.actionId = intent.actionId;
    const title = friendlyIntentTitle(intent, events);
    const sequence = item.querySelector<HTMLElement>(".sequence")!;
    sequence.textContent = String(index + 1);
    sequence.setAttribute("aria-label", `Etapa ${index + 1}`);
    item.querySelector("strong")!.textContent = title;
    item.querySelector("small")!.textContent = `Registrada em ${formatElapsed(intent.elapsedMs)}`;
    const input = item.querySelector<HTMLInputElement>("input")!;
    input.dataset.actionId = intent.actionId;
    if (document.activeElement !== input) {
      input.value = comments.find((comment) => comment.actionId === intent.actionId)?.text ?? "";
    }
    const thumbnail = item.querySelector<HTMLImageElement>(".step-thumbnail")!;
    const asset = assets.find((candidate) => candidate.metadata.actionId === intent.actionId);
    if (asset === undefined) {
      thumbnail.hidden = true;
      thumbnail.removeAttribute("src");
      thumbnail.alt = "";
    } else {
      const url = URL.createObjectURL(new Blob(
        [asset.thumbnail.slice().buffer as ArrayBuffer],
        { type: "image/webp" }
      ));
      timelineObjectUrls.push(url);
      thumbnail.src = url;
      thumbnail.alt = `Miniatura da etapa ${index + 1}: ${title}`;
      thumbnail.hidden = false;
    }
    timeline.append(item);
  });
  for (const [actionId, item] of existing) {
    if (!retained.has(actionId)) item.remove();
  }
}

async function resolveIssue(event: Event): Promise<void> {
  const button = (event.target as Element | null)?.closest<HTMLButtonElement>("button[data-issue-id]");
  if (button?.dataset.issueId === undefined) return;
  await invokeAndRender({ type: "RECORDER_RESOLVE_ISSUE", issueId: button.dataset.issueId });
}

async function updateComment(event: Event): Promise<void> {
  if (!(event.target instanceof HTMLInputElement)) return;
  const input = event.target;
  const actionId = input.dataset.actionId;
  if (actionId === undefined) return;
  comments = comments.filter((comment) => comment.actionId !== actionId);
  const text = input.value.trim();
  if (text.length > 0) comments.push({
    id: stableId("comment", actionId),
    actionId,
    text: text.slice(0, 1_000)
  });
  await chrome.storage.session.set({ "rpablockly.recorder.comments.v1": comments });
}

function showEvidence(index: number): void {
  revokeSlideshowObjectUrls();
  const slideshow = element<HTMLDivElement>("slideshow");
  slideshow.replaceChildren();
  const items = slideshowItems(evidence);
  evidenceIndex = items.length === 0 ? 0 : (index + items.length) % items.length;
  const item = items[evidenceIndex];
  if (item === undefined) {
    const empty = document.createElement("p");
    empty.textContent = "Nenhuma evidência capturada.";
    slideshow.append(empty);
  } else {
    const asset = evidence.find((candidate) => candidate.metadata.id === item.id)!;
    const url = URL.createObjectURL(new Blob(
      [asset.image.slice().buffer as ArrayBuffer],
      { type: "image/webp" }
    ));
    slideshowObjectUrls.push(url);
    const image = document.createElement("img");
    image.src = url;
    image.alt = item.alt;
    slideshow.append(image);
  }
  setEnabled("previous-evidence", items.length > 1);
  setEnabled("next-evidence", items.length > 1);
  setEnabled("remove-evidence", items.length > 0);
}

async function removeCurrentEvidence(): Promise<void> {
  const current = slideshowItems(evidence)[evidenceIndex];
  const asset = current === undefined
    ? undefined
    : evidence.find((candidate) => candidate.metadata.id === current.id);
  if (asset === undefined) return;
  await evidenceStore.delete(asset.metadata.id);
  if (currentCheckpoint === undefined) {
    evidence = evidence.filter((candidate) => candidate.metadata.id !== asset.metadata.id);
    showEvidence(Math.min(evidenceIndex, evidence.length - 1));
  } else {
    await render(currentCheckpoint);
  }
}

async function send(request: RecorderRequest): Promise<Extract<RecorderResponse, { ok: true }>> {
  const response = await chrome.runtime.sendMessage(request) as RecorderResponse;
  if (!response.ok) throw new Error(response.error);
  return response;
}

function scheduleCheckpointRender(checkpoint: RecorderCheckpoint): void {
  pendingCheckpoint = checkpoint;
  if (checkpointRenderTimer !== undefined) return;
  checkpointRenderTimer = globalThis.setTimeout(() => {
    checkpointRenderTimer = undefined;
    const latest = pendingCheckpoint;
    pendingCheckpoint = undefined;
    if (latest !== undefined) void render(latest).catch(showError);
  }, 120);
}

async function refreshRecorderUi(): Promise<void> {
  const response = await send({ type: "RECORDER_GET_STATE" });
  await render(response.checkpoint);
  await refreshActiveTarget();
}

async function refreshActiveTarget(): Promise<void> {
  if (currentCheckpoint !== undefined) {
    targetChecking = false;
    const response = await send({ type: "RECORDER_GET_TARGET" });
    if (response.accessNotice !== undefined) {
      renderAccessNotice(response.accessNotice, currentCheckpoint.state);
      syncControls();
      return;
    }
    setPageTarget(
      "ready",
      "Navegação HTTP(S) autorizada para esta sessão",
      pageLabel(currentCheckpoint.events.at(-1)?.url ?? currentCheckpoint.origin),
      "✓"
    );
    syncControls();
    return;
  }
  targetChecking = true;
  currentTarget = undefined;
  setPageTarget(
    "checking",
    "Verificando a página ativa…",
    "O Recorder precisa de uma página HTTP(S) aberta.",
    "…"
  );
  syncControls();
  try {
    const [targetResponse, tabs] = await Promise.all([
      send({ type: "RECORDER_GET_TARGET" }),
      chrome.tabs.query({ active: true, lastFocusedWindow: true })
    ]);
    const [tab] = tabs;
    const selectedTarget = targetResponse.target?.tabId === tab?.id
      ? targetResponse.target
      : undefined;
    const urlText = tab?.url ?? tab?.pendingUrl ?? selectedTarget?.url;
    if (tab?.id === undefined || urlText === undefined) {
      setPageTarget(
        "blocked",
        tab?.id === undefined
          ? "Nenhuma aba ativa foi encontrada"
          : "O Chrome ainda não liberou os dados desta aba",
        tab?.id === undefined
          ? "Abra um site HTTP(S) em uma aba normal do navegador."
          : "Clique em Iniciar e autorize o acesso às páginas HTTP(S).",
        "!"
      );
      return;
    }
    let url: URL;
    try {
      url = new URL(urlText);
    } catch {
      setPageTarget(
        "blocked",
        "O endereço da página não pôde ser identificado",
        "Abra um site HTTP(S) e clique novamente no ícone do Recorder.",
        "!"
      );
      return;
    }
    if (!/^https?:$/u.test(url.protocol)) {
      setPageTarget(
        "blocked",
        "Esta página do navegador não pode ser gravada",
        "Abra um site HTTP(S) e clique no ícone do Recorder nessa página.",
        "!"
      );
      return;
    }
    currentTarget = {
      tabId: tab.id,
      windowId: tab.windowId,
      url: url.href,
      origin: url.origin
    };
    setPageTarget("ready", "Página pronta para gravar", pageLabel(url.href), "✓");
  } finally {
    targetChecking = false;
    syncControls();
  }
}

function renderAccessNotice(
  notice: RecorderAccessNotice,
  state: RecorderCheckpoint["state"]
): void {
  setPageTarget(
    "blocked",
    "Acesso amplo precisa ser concedido novamente",
    `${pageLabel(notice.url)} — ${accessRecoveryInstruction()}`,
    "!"
  );
  restoreAccessButton.hidden = false;
  setStatus(
    `${state === "recording" ? "A gravação foi interrompida" : "A sessão está pausada"} para não perder ações. ${accessRecoveryInstruction()}`,
    true
  );
}

function accessRecoveryInstruction(): string {
  return "Clique em Restabelecer acesso amplo; depois retome a sessão.";
}

function setPageTarget(
  state: "checking" | "ready" | "blocked",
  title: string,
  detail: string,
  icon: string
): void {
  pageTarget.dataset.state = state;
  pageTargetTitle.textContent = title;
  pageTargetDetail.textContent = detail;
  pageTargetIcon.textContent = icon;
  restoreAccessButton.hidden = true;
}

function syncControls(): void {
  const state = currentCheckpoint?.state ?? "idle";
  setEnabled(
    "start",
    currentCheckpoint === undefined && !targetChecking && !startInProgress
  );
  if (!startInProgress) {
    element<HTMLButtonElement>("start").textContent = "Iniciar";
  }
  setEnabled("pause", state === "recording");
  setEnabled("resume", state === "paused");
  setEnabled("finalize", state === "recording" || state === "paused");
  setEnabled("cancel", currentCheckpoint !== undefined);
}

function updateRecordingFeedback(
  checkpoint: RecorderCheckpoint,
  intents: NormalizedIntent[]
): void {
  const recording = checkpoint.state === "recording";
  recordingIndicator.hidden = !recording;
  recordingSummary.hidden = !recording;
  if (!recording) return;
  const count = intents.length;
  const last = intents.at(-1);
  recordingSummary.textContent = `${count} ${count === 1 ? "etapa registrada" : "etapas registradas"}. ${
    last === undefined ? "Continue usando a página." : `Última: ${friendlyIntentTitle(last, checkpoint.events)}.`
  }`;
}

function friendlyIntentTitle(intent: NormalizedIntent, events: RawCaptureEvent[]): string {
  const target = targetLabel(intent, events);
  const quotedTarget = target === undefined ? undefined : `“${target}”`;
  switch (intent.type) {
    case "navigate":
      return `Abrir ${pageLabel(intent.url)}`;
    case "click":
      return quotedTarget === undefined ? "Clicar na página" : `Clicar em ${quotedTarget}`;
    case "fill":
      return quotedTarget === undefined ? "Preencher um campo" : `Preencher ${quotedTarget}`;
    case "selectOption":
      return quotedTarget === undefined ? "Selecionar uma opção" : `Selecionar em ${quotedTarget}`;
    case "setChecked":
      return quotedTarget === undefined ? "Alterar uma marcação" : `Alterar ${quotedTarget}`;
    case "pressKey":
      return quotedTarget === undefined ? intent.name : `${intent.name} em ${quotedTarget}`;
    case "clickAndSwitchPage":
      return quotedTarget === undefined
        ? "Abrir uma nova página"
        : `Abrir nova página por ${quotedTarget}`;
    case "switchPage":
      return `Trocar para ${pageLabel(intent.url)}`;
    case "closePage":
      return "Fechar a página atual";
    case "upload":
      return quotedTarget === undefined ? "Selecionar um arquivo" : `Selecionar arquivo em ${quotedTarget}`;
    case "download":
      return quotedTarget === undefined ? "Baixar um arquivo" : `Baixar arquivo por ${quotedTarget}`;
  }
}

function targetLabel(intent: NormalizedIntent, events: RawCaptureEvent[]): string | undefined {
  const event = intent.eventIds
    .map((eventId) => events.find((candidate) => candidate.id === eventId))
    .find((candidate) => candidate?.target !== undefined);
  const target = event?.target;
  const value = target?.accessibleName ?? target?.attributes["aria-label"] ??
    target?.attributes.name ?? target?.attributes.placeholder ?? target?.text;
  if (value === undefined) return undefined;
  const normalized = value.replace(/\s+/gu, " ").trim();
  return normalized.length === 0 ? undefined : normalized.slice(0, 80);
}

function pageLabel(value: string): string {
  try {
    const url = new URL(value);
    const path = url.pathname === "/" ? "" : url.pathname;
    return `${url.host}${path}`.slice(0, 100);
  } catch {
    return "a página autorizada";
  }
}

function formatElapsed(elapsedMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(elapsedMs / 1_000));
  const hours = Math.floor(totalSeconds / 3_600);
  const minutes = Math.floor(totalSeconds % 3_600 / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
    : `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

async function waitForDownload(downloadId: number): Promise<void> {
  const current = (await chrome.downloads.search({ id: downloadId }))[0];
  if (current?.state === "complete") return;
  if (current?.state === "interrupted" || isBlockedDownloadDanger(current?.danger)) {
    throw new Error("O Chrome não confirmou o download.");
  }
  await new Promise<void>((resolve, reject) => {
    const listener = (delta: chrome.downloads.DownloadDelta): void => {
      if (delta.id !== downloadId) return;
      if (exportCancelled) {
        chrome.downloads.onChanged.removeListener(listener);
        void chrome.downloads.cancel(downloadId);
        reject(new Error("Exportação cancelada; a sessão foi preservada."));
      } else if (delta.state?.current === "complete") {
        chrome.downloads.onChanged.removeListener(listener);
        resolve();
      } else if (delta.state?.current === "interrupted" ||
          isBlockedDownloadDanger(delta.danger?.current)) {
        chrome.downloads.onChanged.removeListener(listener);
        reject(new Error("O Chrome não confirmou o download."));
      }
    };
    chrome.downloads.onChanged.addListener(listener);
    void chrome.downloads.search({ id: downloadId }).then(([item]) => {
      if (item?.state === "complete") {
        chrome.downloads.onChanged.removeListener(listener);
        resolve();
      } else if (item?.state === "interrupted" || isBlockedDownloadDanger(item?.danger)) {
        chrome.downloads.onChanged.removeListener(listener);
        reject(new Error("O Chrome não confirmou o download."));
      }
    }, reject);
  });
}

function isBlockedDownloadDanger(value: string | undefined): boolean {
  return new Set([
    "file", "url", "content", "host", "unwanted", "blockedTooLarge",
    "sensitiveContentBlock", "deepScannedFailed", "accountCompromise",
    "blockedScanFailed"
  ]).has(value ?? "");
}

async function loadComments(): Promise<BundleComment[]> {
  const result = await chrome.storage.session.get("rpablockly.recorder.comments.v1");
  return (result["rpablockly.recorder.comments.v1"] as BundleComment[] | undefined) ?? [];
}

async function clearComments(): Promise<void> {
  comments = [];
  await chrome.storage.session.remove("rpablockly.recorder.comments.v1");
}

function checkCancelled(): void {
  if (exportCancelled) throw new Error("Exportação cancelada; a sessão foi preservada.");
}

function setProgress(value: number, message: string): void {
  exportProgress.value = value;
  exportMessage.textContent = message;
}

function setStatus(message: string, error = false): void {
  status.textContent = message;
  if (error) status.dataset.tone = "error";
  else delete status.dataset.tone;
}

function setEnabled(id: string, enabled: boolean): void {
  element<HTMLButtonElement>(id).disabled = !enabled;
}

function setConfigurationEnabled(enabled: boolean): void {
  for (const id of [
    "session-name", "capture-screenshots", "include-uploads", "capture-secrets",
    "secret-mode-simple", "secret-mode-advanced", "secret-sharing-password",
    "generate-password", "generate-recovery-key", "recovery-copied",
    "recipient-key-id", "recipient-public-key", "privacy-accepted"
  ]) {
    element<HTMLInputElement | HTMLTextAreaElement | HTMLButtonElement>(id).disabled = !enabled;
  }
}

function syncSecretOptions(): void {
  secretOptions.hidden = !secretToggle.checked;
  if (!secretToggle.checked) clearSensitiveAccess();
  syncSecretMode();
}

function syncSecretMode(): void {
  const simple = element<HTMLInputElement>("secret-mode-simple").checked;
  simpleSecretOptions.hidden = !simple;
  advancedSecretOptions.hidden = simple;
  if (!simple) invalidateGeneratedRecipientAccess();
}

function generatePassword(): void {
  sharingPassword.value = generateSharingPassword();
  sharingPassword.type = "text";
  const toggle = element<HTMLButtonElement>("toggle-password");
  toggle.textContent = "Ocultar";
  toggle.setAttribute("aria-pressed", "true");
  invalidateGeneratedRecipientAccess();
  setStatus("Senha segura gerada. Agora gere a chave de recuperação.");
}

function togglePasswordVisibility(): void {
  const visible = sharingPassword.type === "text";
  sharingPassword.type = visible ? "password" : "text";
  const button = element<HTMLButtonElement>("toggle-password");
  button.textContent = visible ? "Mostrar" : "Ocultar";
  button.setAttribute("aria-pressed", String(!visible));
}

async function prepareSimpleRecipientAccess(): Promise<void> {
  const button = element<HTMLButtonElement>("generate-recovery-key");
  button.disabled = true;
  setStatus("Gerando a proteção criptográfica local…");
  try {
    generatedRecipientAccess = await generateRecipientAccess(sharingPassword.value);
    recoveryKey.value = generatedRecipientAccess.recoveryKey;
    recoveryOutput.hidden = false;
    element<HTMLInputElement>("recovery-copied").checked = false;
    setStatus("Chave gerada. Copie a senha e a chave de recuperação antes de iniciar.");
  } finally {
    button.disabled = false;
  }
}

function invalidateGeneratedRecipientAccess(): void {
  generatedRecipientAccess = undefined;
  recoveryKey.value = "";
  recoveryOutput.hidden = true;
  element<HTMLInputElement>("recovery-copied").checked = false;
}

function clearSensitiveAccess(): void {
  sharingPassword.value = "";
  sharingPassword.type = "password";
  const toggle = element<HTMLButtonElement>("toggle-password");
  toggle.textContent = "Mostrar";
  toggle.setAttribute("aria-pressed", "false");
  invalidateGeneratedRecipientAccess();
  element<HTMLInputElement>("recipient-key-id").value = "";
  element<HTMLTextAreaElement>("recipient-public-key").value = "";
}

async function copyPassword(): Promise<void> {
  if (sharingPassword.value.length === 0) {
    setStatus("Informe ou gere uma senha antes de copiar.", true);
    return;
  }
  await copySensitiveText(sharingPassword.value, "Senha copiada. Evite mantê-la no histórico da área de transferência.");
}

async function copyRecoveryKey(): Promise<void> {
  if (recoveryKey.value.length === 0) {
    setStatus("Gere a chave de recuperação antes de copiar.", true);
    return;
  }
  await copySensitiveText(recoveryKey.value, "Chave de recuperação copiada.");
}

async function copySensitiveText(value: string, message: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    setStatus(message);
  } catch {
    setStatus("O Chrome não permitiu copiar. Selecione o conteúdo do campo manualmente.", true);
  }
}

function showError(error: unknown): void {
  setStatus(error instanceof Error ? error.message : "Operação indisponível.", true);
}

function stateLabel(state: RecorderCheckpoint["state"]): string {
  return ({
    idle: "Pronta para iniciar.",
    recording: "Gravando a navegação em páginas HTTP(S).",
    paused: "Gravação pausada.",
    finalizing: "Finalizando bundle.",
    completed: "Download concluído.",
    failed: "Sessão encerrada com falha."
  })[state];
}

function revokeObjectUrls(): void {
  revokeSlideshowObjectUrls();
  revokeTimelineObjectUrls();
  while (downloadObjectUrls.length > 0) URL.revokeObjectURL(downloadObjectUrls.pop()!);
}

function revokeSlideshowObjectUrls(): void {
  while (slideshowObjectUrls.length > 0) URL.revokeObjectURL(slideshowObjectUrls.pop()!);
}

function revokeTimelineObjectUrls(): void {
  while (timelineObjectUrls.length > 0) URL.revokeObjectURL(timelineObjectUrls.pop()!);
}

function element<T extends HTMLElement>(id: string): T {
  const found = document.getElementById(id);
  if (found === null) throw new Error(`Elemento #${id} ausente.`);
  return found as T;
}
