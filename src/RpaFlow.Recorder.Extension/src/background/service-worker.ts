import type { EvidenceMask } from "../../../../schemas/generated/contracts.js";
import { RecorderCheckpointStore, ChromeSessionStorage } from "../core/checkpoint-store.js";
import { stableId } from "../core/stable.js";
import { createCheckpoint, transition } from "../core/state-machine.js";
import type { RawCaptureEvent, RecorderCheckpoint } from "../core/types.js";
import { ScreenshotRateLimiter, EvidenceStore, createEvidenceAsset } from "../evidence/evidence.js";
import { generatePackage, assertFinalizable } from "../package/generator.js";
import { validateGeneratedPackage } from "../package/validator.js";
import { EncryptedSecretStore } from "../security/secret-store.js";
import { encryptSecret, validateRecipientKey } from "../security/secrets.js";
import { isRecorderRequest, type RecorderRequest, type RecorderResponse } from "../shared/messages.js";
import { UploadStore } from "../uploads/upload-store.js";
import { validateUploadTotals } from "../uploads/uploads.js";

const checkpointStore = new RecorderCheckpointStore(new ChromeSessionStorage());
const evidenceStore = new EvidenceStore();
const secretStore = new EncryptedSecretStore();
const uploadStore = new UploadStore();
const screenshotLimiter = new ScreenshotRateLimiter();
let queue = Promise.resolve();

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
  void chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true })
    .catch(() => undefined);
  chrome.action.onClicked.addListener((tab) => {
    if (tab.windowId !== undefined) {
      void chrome.sidePanel.open({ windowId: tab.windowId }).catch(() => undefined);
    }
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
  }).catch(() => undefined);
});

chrome.tabs.onUpdated.addListener((changedTabId, changeInfo, tab) => {
  if (changeInfo.status !== "complete" || tab.url === undefined || !/^https?:/u.test(tab.url)) return;
  const updatedUrl = tab.url;
  queue = queue.then(async () => {
    const checkpoint = await checkpointStore.load();
    if (checkpoint?.state !== "recording" ||
        !checkpoint.events.some((event) => event.tabId === tabId(changedTabId))) return;
    const origin = new URL(updatedUrl).origin;
    const permitted = await chrome.permissions.contains({ origins: [`${origin}/*`] });
    if (!permitted) {
      await appendBrowserEvent(checkpoint, changedTabId, updatedUrl, "unsupported", {
        unsupportedCode: "CROSS_ORIGIN_FRAME_NOT_CAPTURED",
        unsupportedReason: `A navegação mudou para a origem não autorizada ${origin}.`
      });
      return;
    }
    const recent = [...checkpoint.events].reverse().find((event) => event.tabId === tabId(changedTabId));
    if (recent?.type !== "navigation" || recent.url !== updatedUrl) {
      await appendBrowserEvent(checkpoint, changedTabId, updatedUrl, "navigation", {
        ...(recent?.type === "click" ? { causalEventId: recent.id } : {}),
        navigationKind: "traditional"
      });
    }
    await injectRecorder(changedTabId, checkpoint.options);
  }).catch(() => undefined);
});

async function handleRequest(request: RecorderRequest, sender: chrome.runtime.MessageSender): Promise<RecorderResponse> {
  switch (request.type) {
    case "RECORDER_GET_STATE":
      {
        const checkpoint = await checkpointStore.load();
        return { ok: true, ...(checkpoint === undefined ? {} : { checkpoint }) };
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
  const requestedOrigins = { origins: [`${origin}/*`] };
  const alreadyGranted = await chrome.permissions.contains(requestedOrigins);
  const granted = alreadyGranted
    ? true
    : await chrome.permissions.request(requestedOrigins);
  if (!granted) throw new Error("A permissão para a origem ativa não foi concedida.");
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tab?.id === undefined || tab.url === undefined || new URL(tab.url).origin !== origin) {
    throw new Error("A aba ativa não corresponde à origem autorizada.");
  }
  await Promise.all([evidenceStore.clear(), secretStore.clear(), uploadStore.clear()]);
  let checkpoint = createCheckpoint(request.name, origin, request.options);
  checkpoint.acceptedPrivacyNotices = ["recorder-privacy-v1"];
  checkpoint = transition(checkpoint, "recording");
  const initial: RawCaptureEvent = {
    id: stableId("event", checkpoint.sessionId, 1, "navigation", tab.url),
    sequence: 1,
    elapsedMs: 0,
    capturedAtUtc: checkpoint.startedAtUtc,
    tabId: tabId(tab.id),
    frameId: frameId(tab.id, 0),
    url: tab.url,
    type: "navigation",
    trusted: true,
    navigationKind: "traditional"
  };
  checkpoint = { ...checkpoint, events: [initial], nextSequence: 2 };
  await checkpointStore.save(checkpoint);
  await injectRecorder(tab.id, request.options);
  return { ok: true, checkpoint };
}

async function injectRecorder(tab: number, options: RecorderCheckpoint["options"]): Promise<void> {
  await chrome.scripting.executeScript({
    target: { tabId: tab, allFrames: true },
    files: ["content/content-script.js"]
  });
  await chrome.tabs.sendMessage(tab, { type: "RECORDER_CONFIGURE_CONTENT", options } satisfies RecorderRequest)
    .catch(() => undefined);
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
  const updated: RecorderCheckpoint = {
    ...checkpoint,
    nextSequence: checkpoint.nextSequence + 1,
    events: [...checkpoint.events, event],
    lastCheckpointAtUtc: now.toISOString()
  };
  await checkpointStore.save(updated);
  if (updated.options.captureScreenshots && sender.tab.windowId !== undefined) {
    await captureScreenshot(updated, event, request.event.id, sender).catch(() => undefined);
  }
  return { ok: true, checkpoint: updated };
}

async function captureScreenshot(
  checkpoint: RecorderCheckpoint,
  event: RawCaptureEvent,
  sourceEventId: string,
  sender: chrome.runtime.MessageSender
): Promise<void> {
  if (!screenshotLimiter.tryAcquire(Date.now()) || sender.tab?.id === undefined || sender.tab.windowId === undefined) return;
  const currentTab = await chrome.tabs.get(sender.tab.id);
  if (!currentTab.active || currentTab.windowId !== sender.tab.windowId) return;
  const generated = generatePackage(checkpoint.name, checkpoint.events, checkpoint.resolvedIssueIds);
  const intent = generated.intents.find((candidate) => candidate.eventIds.includes(event.id));
  if (intent === undefined) return;
  let masks: EvidenceMask[] = [];
  try {
    const response = await chrome.tabs.sendMessage(
      sender.tab.id,
      { type: "RECORDER_PREPARE_SCREENSHOT", eventId: sourceEventId } satisfies RecorderRequest,
      { frameId: sender.frameId ?? 0 }
    ) as { ok?: boolean; masks?: EvidenceMask[] };
    masks = response.masks ?? [];
    const dataUrl = await chrome.tabs.captureVisibleTab(sender.tab.windowId, { format: "png" });
    const asset = await createEvidenceAsset(dataUrl, event.id, intent.actionId, event.capturedAtUtc, masks);
    if ((await evidenceStore.list()).length < 200) await evidenceStore.put(asset);
  } finally {
    await chrome.tabs.sendMessage(
      sender.tab.id,
      { type: "RECORDER_CLEAR_SCREENSHOT", eventId: sourceEventId } satisfies RecorderRequest,
      { frameId: sender.frameId ?? 0 }
    ).catch(() => undefined);
  }
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
  const checkpoint = transition(await requireCheckpoint(), "completed");
  await checkpointStore.save(checkpoint);
  await clearSession();
  return { ok: true };
}

async function fail(_reason: string): Promise<RecorderResponse> {
  const current = await requireCheckpoint();
  if (current.state !== "finalizing" && current.state !== "recording" && current.state !== "paused") {
    return { ok: true, checkpoint: current };
  }
  const checkpoint = transition(current, "failed");
  await checkpointStore.save(checkpoint);
  await clearSensitiveStores();
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
  await Promise.all([
    checkpointStore.clear(), evidenceStore.clear(), secretStore.clear(), uploadStore.clear()
  ]);
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
): Promise<void> {
  const now = new Date();
  if (elapsed(checkpoint, now) > 480 * 60 * 1_000) {
    const failed = transition(checkpoint, "failed");
    await checkpointStore.save(failed);
    await clearSensitiveStores();
    return;
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
  await checkpointStore.save({
    ...checkpoint,
    nextSequence: checkpoint.nextSequence + 1,
    events: [...checkpoint.events, event],
    lastCheckpointAtUtc: now.toISOString()
  });
}
