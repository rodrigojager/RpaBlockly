import type { EvidenceMask } from "../../../../schemas/generated/contracts.js";
import { RecorderCheckpointStore, ChromeSessionStorage } from "../core/checkpoint-store.js";
import { stableId } from "../core/stable.js";
import { createCheckpoint, transition } from "../core/state-machine.js";
import type {
  EvidenceCaptureFailureStage,
  RawCaptureEvent,
  RecorderCheckpoint
} from "../core/types.js";
import {
  ScreenshotRateLimiter,
  EvidenceStore,
  createEvidenceAsset,
  maximumEvidenceItems
} from "../evidence/evidence.js";
import { generatePackage, assertFinalizable } from "../package/generator.js";
import { validateGeneratedPackage } from "../package/validator.js";
import { EncryptedSecretStore } from "../security/secret-store.js";
import { encryptSecret, validateRecipientKey } from "../security/secrets.js";
import {
  isRecorderRequest,
  type RecorderRequest,
  type RecorderResponse,
  type RecorderTarget,
  type RecorderUiRefresh
} from "../shared/messages.js";
import { UploadStore } from "../uploads/upload-store.js";
import { validateUploadTotals } from "../uploads/uploads.js";

const checkpointStore = new RecorderCheckpointStore(new ChromeSessionStorage());
const evidenceStore = new EvidenceStore();
const secretStore = new EncryptedSecretStore();
const uploadStore = new UploadStore();
const screenshotLimiter = new ScreenshotRateLimiter();
const recorderTargetKey = "rpablockly.recorder.target.v1";
const temporaryOriginsKey = "rpablockly.recorder.temporary-origins.v1";
const recorderHttpOrigins = ["http://*/*", "https://*/*"] as const;
const recorderScreenshotOrigins = ["<all_urls>"] as const;
let queue = cleanupOrphanedTemporaryPermissions().catch(() => undefined);

type ScreenshotCaptureResult =
  | { outcome: "captured" }
  | { outcome: "skipped" }
  | { outcome: "failed"; stage: EvidenceCaptureFailureStage; message: string };

interface ScreenshotTarget {
  tabId: number;
  windowId: number;
  frameId: number;
}

chrome.runtime.onMessage.addListener((message: unknown, sender, sendResponse) => {
  if (!isRecorderRequest(message) || sender.id !== chrome.runtime.id) return false;
  queue = queue.then(async () => await handleRequest(message, sender))
    .then(sendResponse)
    .catch((error: unknown) => sendResponse({
      ok: false,
      error: error instanceof Error ? error.message : "Falha inesperada no Recorder."
    } satisfies RecorderResponse));
  return true;
});

if (chrome.sidePanel !== undefined) {
  void chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: false })
    .catch(() => undefined);
  chrome.action.onClicked.addListener((tab) => {
    if (tab.id === undefined) return;
    void chrome.sidePanel.open({ tabId: tab.id }).catch(() => undefined);
    queue = queue
      .then(async () => await rememberRecorderTarget(tab))
      .then(async () => await notifyUiRefresh())
      .catch(() => undefined);
  });
}

chrome.tabs.onCreated.addListener((tab) => {
  queue = queue.then(async () => {
    const checkpoint = await checkpointStore.load();
    if (checkpoint?.state !== "recording" || tab.id === undefined || tab.openerTabId === undefined) return;
    const causal = [...checkpoint.events].reverse().find((event) =>
      event.tabId === tabId(tab.openerTabId!) && event.type === "click");
    if (causal === undefined) return;
    const now = new Date();
    const event: RawCaptureEvent = {
      id: stableId("event", checkpoint.sessionId, checkpoint.nextSequence, "popup", tab.id),
      sequence: checkpoint.nextSequence,
      elapsedMs: elapsed(checkpoint, now),
      capturedAtUtc: now.toISOString(),
      tabId: tabId(tab.id),
      frameId: frameId(tab.id, 0),
      url: tab.url ?? "about:blank",
      type: "popup",
      trusted: true,
      causalEventId: causal.id,
      navigationKind: "popup"
    };
    await checkpointStore.save({
      ...checkpoint,
      nextSequence: checkpoint.nextSequence + 1,
      events: [...checkpoint.events, event],
      lastCheckpointAtUtc: now.toISOString()
    });
    await notifyUiRefresh();
  }).catch(() => undefined);
});

chrome.tabs.onUpdated.addListener((changedTabId, changeInfo, tab) => {
  if (changeInfo.status !== "complete" || tab.url === undefined || !/^https?:/u.test(tab.url)) return;
  const updatedUrl = tab.url;
  queue = queue.then(async () => {
    const checkpoint = await checkpointStore.load();
    if (checkpoint?.state !== "recording" ||
        !checkpoint.events.some((event) => event.tabId === tabId(changedTabId))) return;
    const permitted = await chrome.permissions.contains({
      origins: requiredRecorderOrigins(checkpoint.options)
    });
    if (!permitted) {
      await appendBrowserEvent(checkpoint, changedTabId, updatedUrl, "unsupported", {
        unsupportedCode: "UNSUPPORTED_INTERACTION",
        unsupportedReason: "A autorização HTTP(S) da sessão foi removida durante a gravação."
      });
      await notifyUiRefresh();
      return;
    }
    const recent = [...checkpoint.events].reverse().find((event) => event.tabId === tabId(changedTabId));
    let current = checkpoint;
    let navigationEvent: RawCaptureEvent | undefined;
    if (recent?.type !== "navigation" || recent.url !== updatedUrl) {
      const appended = await appendBrowserEvent(checkpoint, changedTabId, updatedUrl, "navigation", {
        ...(recent?.type === "click" ? { causalEventId: recent.id } : {}),
        navigationKind: "traditional"
      });
      current = appended?.checkpoint ?? checkpoint;
      navigationEvent = appended?.event;
    }
    await injectRecorder(changedTabId, current.options);
    if (current.options.captureScreenshots && navigationEvent !== undefined) {
      await captureAndRecordScreenshot(
        current,
        navigationEvent,
        navigationEvent.id,
        { tabId: changedTabId, windowId: tab.windowId, frameId: 0 }
      );
    }
    await notifyUiRefresh();
  }).catch(() => undefined);
});

async function handleRequest(request: RecorderRequest, sender: chrome.runtime.MessageSender): Promise<RecorderResponse> {
  switch (request.type) {
    case "RECORDER_GET_STATE":
      {
        const checkpoint = await checkpointStore.load();
        return { ok: true, ...(checkpoint === undefined ? {} : { checkpoint }) };
      }
    case "RECORDER_GET_TARGET":
      {
        const target = await loadRecorderTarget();
        return { ok: true, ...(target === undefined ? {} : { target }) };
      }
    case "RECORDER_START":
      return await start(request);
    case "RECORDER_PAUSE":
      return await changeState("paused");
    case "RECORDER_RESUME":
      return await changeState("recording");
    case "RECORDER_FINALIZE":
      return await finalize();
    case "RECORDER_COMPLETE":
      return await complete();
    case "RECORDER_ABORT_FINALIZE":
      return await changeState("paused");
    case "RECORDER_FAIL":
      return await fail(request.reason);
    case "RECORDER_CANCEL":
      await clearSession();
      return { ok: true };
    case "RECORDER_RESOLVE_ISSUE":
      return await resolveIssue(request.issueId);
    case "RECORDER_CAPTURE_EVENT":
      return await appendCapturedEvent(request, sender);
    case "RECORDER_CONFIGURE_CONTENT":
    case "RECORDER_PREPARE_SCREENSHOT":
    case "RECORDER_CLEAR_SCREENSHOT":
      return { ok: false, error: "Mensagem reservada ao content script." };
  }
}

async function rememberRecorderTarget(tab: chrome.tabs.Tab): Promise<void> {
  const urlText = tab.url ?? tab.pendingUrl;
  if (tab.id === undefined || urlText === undefined) {
    await chrome.storage.session.remove(recorderTargetKey);
    return;
  }
  let url: URL;
  try {
    url = new URL(urlText);
  } catch {
    await chrome.storage.session.remove(recorderTargetKey);
    return;
  }
  if (!/^https?:$/u.test(url.protocol)) {
    await chrome.storage.session.remove(recorderTargetKey);
    return;
  }
  await chrome.storage.session.set({
    [recorderTargetKey]: {
      tabId: tab.id,
      windowId: tab.windowId,
      url: url.href,
      origin: url.origin
    } satisfies RecorderTarget
  });
}

async function loadRecorderTarget(): Promise<RecorderTarget | undefined> {
  const result = await chrome.storage.session.get(recorderTargetKey);
  const value = result[recorderTargetKey];
  if (value === null || typeof value !== "object") return undefined;
  const candidate = value as Partial<RecorderTarget>;
  if (!Number.isInteger(candidate.tabId) || !Number.isInteger(candidate.windowId) ||
      typeof candidate.url !== "string" || typeof candidate.origin !== "string") {
    return undefined;
  }
  return candidate as RecorderTarget;
}

async function start(request: Extract<RecorderRequest, { type: "RECORDER_START" }>): Promise<RecorderResponse> {
  const existing = await checkpointStore.load();
  if (existing !== undefined && !["completed", "failed"].includes(existing.state)) {
    throw new Error("Já existe uma sessão ativa. Cancele-a antes de iniciar outra.");
  }
  const origin = new URL(request.origin).origin;
  if (!/^https?:$/u.test(new URL(origin).protocol)) throw new Error("Somente origens HTTP(S) são suportadas.");
  if (request.options.captureSecrets) {
    if (request.options.recipientKeyId === undefined || request.options.recipientPublicKeyPem === undefined) {
      throw new Error("Captura de segredos exige key ID e chave pública do destinatário.");
    }
    await validateRecipientKey({
      keyId: request.options.recipientKeyId,
      pem: request.options.recipientPublicKeyPem
    });
  }
  if (!await chrome.permissions.contains({ origins: requiredRecorderOrigins(request.options) })) {
    throw new Error(request.options.captureScreenshots
      ? "Autorize a captura visual temporária pelo painel antes de iniciar."
      : "Autorize a gravação em páginas HTTP(S) pelo painel antes de iniciar.");
  }
  const temporaryOrigins = validateTemporaryOrigins(request.temporaryOrigins);
  const tab = await chrome.tabs.get(request.tabId);
  if (!tab.active || tab.url === undefined || new URL(tab.url).origin !== origin) {
    throw new Error("A página escolhida deixou de ser a aba ativa ou mudou de origem.");
  }
  if (temporaryOrigins.length > 0) {
    await chrome.storage.session.set({ [temporaryOriginsKey]: temporaryOrigins });
  }
  try {
    await Promise.all([evidenceStore.clear(), secretStore.clear(), uploadStore.clear()]);
    screenshotLimiter.reset();
    let checkpoint = createCheckpoint(request.name, origin, request.options);
    checkpoint.acceptedPrivacyNotices = ["recorder-privacy-v2"];
    checkpoint = transition(checkpoint, "recording");
    const initial: RawCaptureEvent = {
      id: stableId("event", checkpoint.sessionId, 1, "navigation", tab.url),
      sequence: 1,
      elapsedMs: 0,
      capturedAtUtc: checkpoint.startedAtUtc,
      tabId: tabId(request.tabId),
      frameId: frameId(request.tabId, 0),
      url: tab.url,
      type: "navigation",
      trusted: true,
      navigationKind: "traditional"
    };
    checkpoint = { ...checkpoint, events: [initial], nextSequence: 2 };
    await injectRecorder(request.tabId, request.options);
    await checkpointStore.save(checkpoint);
    if (checkpoint.options.captureScreenshots) {
      checkpoint = await captureAndRecordScreenshot(
        checkpoint,
        initial,
        initial.id,
        { tabId: request.tabId, windowId: tab.windowId, frameId: 0 }
      );
      await notifyUiRefresh();
    }
    return { ok: true, checkpoint };
  } catch {
    if (temporaryOrigins.length > 0) await releaseTemporaryPermissions();
    throw new Error(
      "Não foi possível ativar o gravador nesta página. Recarregue a aba e tente novamente."
    );
  }
}

function validateTemporaryOrigins(origins: string[]): string[] {
  const unique = [...new Set(origins)];
  const allowed = new Set<string>([...recorderHttpOrigins, ...recorderScreenshotOrigins]);
  if (unique.length !== origins.length ||
      unique.some((origin) => !allowed.has(origin))) {
    throw new Error("A lista de permissões temporárias da sessão é inválida.");
  }
  return unique;
}

function requiredRecorderOrigins(options: RecorderCheckpoint["options"]): string[] {
  return options.captureScreenshots
    ? [...recorderScreenshotOrigins]
    : [...recorderHttpOrigins];
}

async function injectRecorder(tab: number, options: RecorderCheckpoint["options"]): Promise<void> {
  await chrome.scripting.executeScript({
    target: { tabId: tab, allFrames: true },
    files: ["content/content-script.js"]
  });
  await chrome.tabs.sendMessage(
    tab,
    { type: "RECORDER_CONFIGURE_CONTENT", options } satisfies RecorderRequest
  );
}

async function appendCapturedEvent(
  request: Extract<RecorderRequest, { type: "RECORDER_CAPTURE_EVENT" }>,
  sender: chrome.runtime.MessageSender
): Promise<RecorderResponse> {
  const checkpoint = await requireCheckpoint();
  if (checkpoint.state !== "recording") return { ok: true, checkpoint };
  if (!request.event.trusted || sender.tab?.id === undefined) throw new Error("Evento não confiável rejeitado.");
  const now = new Date();
  const { causalEventId: _sourceCausalEventId, ...source } = request.event;
  const priorCausal = request.event.causalEventId === undefined
    ? undefined
    : [...checkpoint.events].reverse().find((event) =>
      event.tabId === tabId(sender.tab!.id!) && event.elapsedMs <= elapsed(checkpoint, now));
  let event: RawCaptureEvent = {
    ...source,
    id: stableId("event", checkpoint.sessionId, checkpoint.nextSequence, source.id),
    sequence: checkpoint.nextSequence,
    elapsedMs: elapsed(checkpoint, now),
    capturedAtUtc: now.toISOString(),
    tabId: tabId(sender.tab.id),
    frameId: frameId(sender.tab.id, sender.frameId ?? 0),
    ...(priorCausal === undefined ? {} : { causalEventId: priorCausal.id })
  };
  if (request.transientSecret !== undefined) {
    if (!checkpoint.options.captureSecrets ||
        checkpoint.options.recipientKeyId === undefined ||
        checkpoint.options.recipientPublicKeyPem === undefined) {
      throw new Error("Segredo recebido sem consentimento e chave válidos.");
    }
    const reference = `secret.recorded.value_${String(event.sequence).padStart(4, "0")}`;
    const plaintext = new TextEncoder().encode(request.transientSecret);
    const envelope = await encryptSecret(reference, plaintext, {
      keyId: checkpoint.options.recipientKeyId,
      pem: checkpoint.options.recipientPublicKeyPem
    }, checkpoint.sessionId);
    await secretStore.put(envelope);
    event = { ...event, secretReference: reference };
  }
  if (event.upload?.contentBase64 !== undefined) {
    const proposed = event.upload;
    const existing = (await uploadStore.list()).filter((upload) =>
      upload.sha256 === undefined || proposed.sha256 === undefined
        ? upload.name !== proposed.name || upload.size !== proposed.size
        : upload.sha256 !== proposed.sha256);
    try {
      validateUploadTotals([...existing, proposed]);
      await uploadStore.put(proposed);
      const { contentBase64: _content, ...metadata } = proposed;
      event = { ...event, upload: metadata };
    } catch (error) {
      const { upload: _upload, ...withoutUpload } = event;
      event = {
        ...withoutUpload,
        type: "unsupported",
        unsupportedCode: "UNSUPPORTED_INTERACTION",
        unsupportedReason: error instanceof Error
          ? error.message
          : "O upload excedeu os limites da sessão."
      };
    }
  }
  let updated: RecorderCheckpoint = {
    ...checkpoint,
    nextSequence: checkpoint.nextSequence + 1,
    events: [...checkpoint.events, event],
    lastCheckpointAtUtc: now.toISOString()
  };
  await checkpointStore.save(updated);
  if (updated.options.captureScreenshots && sender.tab.windowId !== undefined) {
    updated = await captureAndRecordScreenshot(
      updated,
      event,
      request.event.id,
      {
        tabId: sender.tab.id,
        windowId: sender.tab.windowId,
        frameId: sender.frameId ?? 0
      }
    );
    await notifyUiRefresh();
  }
  return { ok: true, checkpoint: updated };
}

async function captureScreenshot(
  checkpoint: RecorderCheckpoint,
  event: RawCaptureEvent,
  sourceEventId: string,
  target: ScreenshotTarget
): Promise<ScreenshotCaptureResult> {
  if (!screenshotLimiter.tryAcquire(Date.now())) return { outcome: "skipped" };
  let currentTab: chrome.tabs.Tab;
  try {
    currentTab = await chrome.tabs.get(target.tabId);
  } catch {
    return screenshotFailure(
      "capture",
      "A aba deixou de existir antes da captura da evidência."
    );
  }
  if (!currentTab.active || currentTab.windowId !== target.windowId) {
    return { outcome: "skipped" };
  }
  const generated = generatePackage(checkpoint.name, checkpoint.events, checkpoint.resolvedIssueIds);
  const intent = generated.intents.find((candidate) => candidate.eventIds.includes(event.id));
  if (intent === undefined) return { outcome: "skipped" };
  let masks: EvidenceMask[] = [];
  let prepared = false;
  try {
    try {
      const response = await chrome.tabs.sendMessage(
        target.tabId,
        { type: "RECORDER_PREPARE_SCREENSHOT", eventId: sourceEventId } satisfies RecorderRequest,
        { frameId: target.frameId }
      ) as { ok?: boolean; masks?: EvidenceMask[] };
      if (response.ok !== true) {
        return screenshotFailure(
          "prepare",
          "A página não confirmou a preparação das máscaras da evidência."
        );
      }
      masks = response.masks ?? [];
      prepared = true;
    } catch {
      return screenshotFailure(
        "prepare",
        "A página mudou antes de preparar as máscaras da evidência."
      );
    }
    let dataUrl: string;
    try {
      dataUrl = await chrome.tabs.captureVisibleTab(target.windowId, { format: "png" });
    } catch {
      return screenshotFailure(
        "capture",
        "O Chrome recusou a captura da aba visível."
      );
    }
    let asset: Awaited<ReturnType<typeof createEvidenceAsset>>;
    try {
      asset = await createEvidenceAsset(
        dataUrl,
        event.id,
        intent.actionId,
        event.capturedAtUtc,
        masks
      );
    } catch {
      return screenshotFailure(
        "process",
        "A imagem foi capturada, mas não pôde ser processada como evidência."
      );
    }
    try {
      if ((await evidenceStore.list()).length >= maximumEvidenceItems) {
        return { outcome: "skipped" };
      }
      await evidenceStore.put(asset);
    } catch {
      return screenshotFailure(
        "store",
        "A imagem foi processada, mas não pôde ser salva no armazenamento local."
      );
    }
    return { outcome: "captured" };
  } finally {
    if (prepared) {
      await chrome.tabs.sendMessage(
        target.tabId,
        { type: "RECORDER_CLEAR_SCREENSHOT", eventId: sourceEventId } satisfies RecorderRequest,
        { frameId: target.frameId }
      ).catch(() => undefined);
    }
  }
}

async function captureAndRecordScreenshot(
  checkpoint: RecorderCheckpoint,
  event: RawCaptureEvent,
  sourceEventId: string,
  target: ScreenshotTarget
): Promise<RecorderCheckpoint> {
  const result = await captureScreenshot(checkpoint, event, sourceEventId, target);
  const evidenceCapture = checkpoint.evidenceCapture ?? {
    attempted: 0,
    captured: 0,
    skipped: 0,
    failed: 0
  };
  const updated: RecorderCheckpoint = {
    ...checkpoint,
    evidenceCapture: result.outcome === "captured"
      ? {
          ...evidenceCapture,
          attempted: evidenceCapture.attempted + 1,
          captured: evidenceCapture.captured + 1
        }
      : result.outcome === "failed"
        ? {
            ...evidenceCapture,
            attempted: evidenceCapture.attempted + 1,
            failed: evidenceCapture.failed + 1,
            lastFailure: {
              eventId: event.id,
              stage: result.stage,
              message: result.message,
              occurredAtUtc: new Date().toISOString()
            }
          }
        : {
            ...evidenceCapture,
            skipped: evidenceCapture.skipped + 1
          },
    lastCheckpointAtUtc: new Date().toISOString()
  };
  await checkpointStore.save(updated);
  return updated;
}

function screenshotFailure(
  stage: EvidenceCaptureFailureStage,
  message: string
): ScreenshotCaptureResult {
  return { outcome: "failed", stage, message };
}

async function changeState(next: "paused" | "recording"): Promise<RecorderResponse> {
  const checkpoint = transition(await requireCheckpoint(), next);
  await checkpointStore.save(checkpoint);
  return { ok: true, checkpoint };
}

async function finalize(): Promise<RecorderResponse> {
  const current = await requireCheckpoint();
  const generated = generatePackage(current.name, current.events, current.resolvedIssueIds);
  assertFinalizable(generated);
  validateGeneratedPackage(generated);
  const checkpoint = transition(current, "finalizing");
  await checkpointStore.save(checkpoint);
  return { ok: true, checkpoint };
}

async function complete(): Promise<RecorderResponse> {
  const current = await requireCheckpoint();
  if (current.state === "completed") {
    await clearSession();
    return { ok: true };
  }
  const checkpoint = transition(current, "completed");
  await checkpointStore.save(checkpoint);
  await clearSession();
  return { ok: true };
}

async function fail(_reason: string): Promise<RecorderResponse> {
  const current = await requireCheckpoint();
  if (current.state === "failed") {
    await releaseTemporaryPermissions();
    return { ok: true, checkpoint: current };
  }
  if (current.state !== "finalizing" && current.state !== "recording" && current.state !== "paused") {
    return { ok: true, checkpoint: current };
  }
  const checkpoint = transition(current, "failed");
  await checkpointStore.save(checkpoint);
  await clearSensitiveStores();
  await releaseTemporaryPermissions();
  return { ok: true, checkpoint };
}

async function resolveIssue(issueId: string): Promise<RecorderResponse> {
  const checkpoint = await requireCheckpoint();
  const updated = {
    ...checkpoint,
    resolvedIssueIds: [...new Set([...checkpoint.resolvedIssueIds, issueId])].sort()
  };
  await checkpointStore.save(updated);
  return { ok: true, checkpoint: updated };
}

async function clearSession(): Promise<void> {
  await releaseTemporaryPermissions();
  await Promise.all([
    checkpointStore.clear(), evidenceStore.clear(), secretStore.clear(), uploadStore.clear()
  ]);
}

async function cleanupOrphanedTemporaryPermissions(): Promise<void> {
  const checkpoint = await checkpointStore.load();
  if (checkpoint === undefined || checkpoint.state === "completed" || checkpoint.state === "failed") {
    await releaseTemporaryPermissions();
  }
}

async function releaseTemporaryPermissions(): Promise<void> {
  const stored = await chrome.storage.session.get(temporaryOriginsKey);
  const raw = stored[temporaryOriginsKey];
  const origins = Array.isArray(raw)
    ? raw.filter((value): value is string =>
      typeof value === "string" &&
      new Set<string>([...recorderHttpOrigins, ...recorderScreenshotOrigins]).has(value))
    : [];
  if (origins.length === 0) {
    await chrome.storage.session.remove(temporaryOriginsKey);
    return;
  }
  const removed = await chrome.permissions.remove({ origins });
  if (!removed && await chrome.permissions.contains({ origins })) {
    throw new Error("Não foi possível retirar a autorização HTTP(S) temporária da sessão.");
  }
  await chrome.storage.session.remove(temporaryOriginsKey);
}

async function clearSensitiveStores(): Promise<void> {
  await Promise.all([evidenceStore.clear(), secretStore.clear(), uploadStore.clear()]);
}

async function requireCheckpoint(): Promise<RecorderCheckpoint> {
  const checkpoint = await checkpointStore.load();
  if (checkpoint === undefined) throw new Error("Não existe sessão ativa.");
  return checkpoint;
}

function elapsed(checkpoint: RecorderCheckpoint, now: Date): number {
  return Math.max(0, now.getTime() - new Date(checkpoint.startedAtUtc).getTime());
}

function tabId(value: number): string {
  return `tab-${value}`;
}

function frameId(tab: number, frame: number): string {
  return `frame-${tab}-${frame}`;
}

async function appendBrowserEvent(
  checkpoint: RecorderCheckpoint,
  browserTabId: number,
  url: string,
  type: RawCaptureEvent["type"],
  details: Partial<RawCaptureEvent>
): Promise<{ checkpoint: RecorderCheckpoint; event: RawCaptureEvent } | undefined> {
  const now = new Date();
  if (elapsed(checkpoint, now) > 480 * 60 * 1_000) {
    const failed = transition(checkpoint, "failed");
    await checkpointStore.save(failed);
    await clearSensitiveStores();
    await releaseTemporaryPermissions();
    return undefined;
  }
  const event: RawCaptureEvent = {
    id: stableId("event", checkpoint.sessionId, checkpoint.nextSequence, type, url),
    sequence: checkpoint.nextSequence,
    elapsedMs: elapsed(checkpoint, now),
    capturedAtUtc: now.toISOString(),
    tabId: tabId(browserTabId),
    frameId: frameId(browserTabId, 0),
    url,
    type,
    trusted: true,
    ...details
  };
  const updated: RecorderCheckpoint = {
    ...checkpoint,
    nextSequence: checkpoint.nextSequence + 1,
    events: [...checkpoint.events, event],
    lastCheckpointAtUtc: now.toISOString()
  };
  await checkpointStore.save(updated);
  return { checkpoint: updated, event };
}

async function notifyUiRefresh(): Promise<void> {
  await chrome.runtime.sendMessage({
    type: "RPABLOCKLY_RECORDER_REFRESH"
  } satisfies RecorderUiRefresh).catch(() => undefined);
}
