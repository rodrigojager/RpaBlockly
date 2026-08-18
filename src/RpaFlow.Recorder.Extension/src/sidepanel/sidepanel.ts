import { buildBundle, verifyBundleIntegrity, type BundleComment } from "../bundle/bundle.js";
import { stableId, slug } from "../core/stable.js";
import type { RecorderCheckpoint, RecorderOptions } from "../core/types.js";
import { EvidenceStore, slideshowItems, type EvidenceAsset } from "../evidence/evidence.js";
import { assertFinalizable, generatePackage } from "../package/generator.js";
import { validateGeneratedPackage } from "../package/validator.js";
import {
  generateRecipientAccess,
  generateSharingPassword,
  type GeneratedRecipientAccess
} from "../security/recovery.js";
import { EncryptedSecretStore } from "../security/secret-store.js";
import type { RecorderRequest, RecorderResponse } from "../shared/messages.js";
import { hydrateUploads, UploadStore } from "../uploads/upload-store.js";

const evidenceStore = new EvidenceStore();
const secretStore = new EncryptedSecretStore();
const uploadStore = new UploadStore();
const objectUrls: string[] = [];
let evidence: EvidenceAsset[] = [];
let evidenceIndex = 0;
let exportCancelled = false;
let activeDownloadId: number | undefined;
let comments: BundleComment[] = [];
let generatedRecipientAccess: GeneratedRecipientAccess | undefined;

const status = element<HTMLParagraphElement>("status");
const issueList = element<HTMLOListElement>("issues");
const timeline = element<HTMLOListElement>("timeline");
const issueCount = element<HTMLSpanElement>("issue-count");
const stepCount = element<HTMLSpanElement>("step-count");
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

void initialize();

async function initialize(): Promise<void> {
  syncSecretOptions();
  comments = await loadComments();
  const response = await send({ type: "RECORDER_GET_STATE" });
  await render(response.checkpoint);
}

async function start(): Promise<void> {
  if (!element<HTMLInputElement>("privacy-accepted").checked) {
    setStatus("Confirme o aviso de privacidade antes de iniciar.", true);
    return;
  }
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tab?.url === undefined || !/^https?:/u.test(tab.url)) {
    setStatus("Abra uma página HTTP(S) antes de iniciar.", true);
    return;
  }
  const origin = new URL(tab.url).origin;
  const requestedOrigins = { origins: [`${origin}/*`] };
  const alreadyGranted = await chrome.permissions.contains(requestedOrigins);
  const granted = alreadyGranted
    ? true
    : await chrome.permissions.request(requestedOrigins);
  if (!granted) {
    setStatus("A permissão para a origem ativa não foi concedida.", true);
    return;
  }
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
    captureScreenshots: element<HTMLInputElement>("capture-screenshots").checked,
    captureSecrets,
    includeUploads: element<HTMLInputElement>("include-uploads").checked,
    ...recipientOptions
  };
  await invokeAndRender({
    type: "RECORDER_START",
    name: element<HTMLInputElement>("session-name").value.trim() || "Nova gravação",
    origin,
    options
  });
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
    objectUrls.push(url);
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
  setStatus("Sessão excluída.");
}

async function invokeAndRender(request: RecorderRequest): Promise<void> {
  try {
    const response = await send(request);
    await render(response.checkpoint);
  } catch (error) {
    setStatus(error instanceof Error ? error.message : "Operação indisponível.", true);
  }
}

async function render(checkpoint: RecorderCheckpoint | undefined): Promise<void> {
  const state = checkpoint?.state ?? "idle";
  setStatus(checkpoint === undefined ? "Nenhuma sessão ativa." : stateLabel(state));
  setEnabled("start", checkpoint === undefined);
  setEnabled("pause", state === "recording");
  setEnabled("resume", state === "paused");
  setEnabled("finalize", state === "recording" || state === "paused");
  setEnabled("cancel", checkpoint !== undefined);
  setConfigurationEnabled(checkpoint === undefined);
  issueList.replaceChildren();
  timeline.replaceChildren();
  if (checkpoint === undefined) {
    issueCount.textContent = "0";
    stepCount.textContent = "0";
    evidence = [];
    showEvidence(0);
    return;
  }
  const generated = generatePackage(checkpoint.name, checkpoint.events, checkpoint.resolvedIssueIds);
  renderIssues(generated.issues);
  renderTimeline(generated.intents);
  evidence = await evidenceStore.list();
  showEvidence(Math.min(evidenceIndex, Math.max(0, evidence.length - 1)));
}

function renderIssues(issues: ReturnType<typeof generatePackage>["issues"]): void {
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

function renderTimeline(intents: ReturnType<typeof generatePackage>["intents"]): void {
  stepCount.textContent = String(intents.length);
  intents.forEach((intent, index) => {
    const fragment = element<HTMLTemplateElement>("step-template").content.cloneNode(true) as DocumentFragment;
    fragment.querySelector(".sequence")!.textContent = String(index + 1);
    fragment.querySelector("strong")!.textContent = intent.name;
    fragment.querySelector("small")!.textContent = `${intent.type} · ${intent.actionId}`;
    const input = fragment.querySelector("input")!;
    input.dataset.actionId = intent.actionId;
    input.value = comments.find((comment) => comment.actionId === intent.actionId)?.text ?? "";
    timeline.append(fragment);
  });
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
  revokeObjectUrls();
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
    objectUrls.push(url);
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
  const asset = evidence[evidenceIndex];
  if (asset === undefined) return;
  await evidenceStore.delete(asset.metadata.id);
  evidence = evidence.filter((candidate) => candidate.metadata.id !== asset.metadata.id);
  showEvidence(Math.min(evidenceIndex, evidence.length - 1));
}

async function send(request: RecorderRequest): Promise<Extract<RecorderResponse, { ok: true }>> {
  const response = await chrome.runtime.sendMessage(request) as RecorderResponse;
  if (!response.ok) throw new Error(response.error);
  return response;
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
  status.style.color = error ? "#fecaca" : "";
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
    recording: "Gravando a origem autorizada.",
    paused: "Gravação pausada.",
    finalizing: "Finalizando bundle.",
    completed: "Download concluído.",
    failed: "Sessão encerrada com falha."
  })[state];
}

function revokeObjectUrls(): void {
  while (objectUrls.length > 0) URL.revokeObjectURL(objectUrls.pop()!);
}

function element<T extends HTMLElement>(id: string): T {
  const found = document.getElementById(id);
  if (found === null) throw new Error(`Elemento #${id} ausente.`);
  return found as T;
}
