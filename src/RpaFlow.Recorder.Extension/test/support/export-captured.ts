import { readFile, writeFile } from "node:fs/promises";
import { buildBundle, verifyBundleIntegrity } from "../../src/bundle/bundle.js";
import { assertFinalizable, generatePackage } from "../../src/package/generator.js";
import { validateGeneratedPackage } from "../../src/package/validator.js";
import type { RawCaptureEvent, RecorderCheckpoint } from "../../src/core/types.js";

const [, , inputPath, outputPath] = process.argv;
if (inputPath === undefined || outputPath === undefined) {
  throw new Error("Uso: export-captured <mensagens.json> <bundle.zip>.");
}
const messages = JSON.parse(await readFile(inputPath, "utf8")) as Array<{
  type?: string;
  event?: RawCaptureEvent;
}>;
const captured = messages
  .filter((message) => message.type === "RECORDER_CAPTURE_EVENT" && message.event !== undefined)
  .map((message) => message.event!);
if (captured.length === 0) throw new Error("Nenhum evento real foi capturado pela extensão.");

const oldToNew = new Map(captured.map((event, index) =>
  [event.id, `event-fixture-${String(index + 1).padStart(3, "0")}`]));
const normalized = captured.map((event, index): RawCaptureEvent => ({
  ...event,
  id: oldToNew.get(event.id)!,
  sequence: index + 2,
  elapsedMs: (index + 1) * 100,
  capturedAtUtc: `2026-08-17T18:00:${String(index + 1).padStart(2, "0")}Z`,
  tabId: "tab-fixture",
  frameId: event.target?.frames.length ? "frame-fixture-iframe" : "frame-fixture-main",
  ...(event.causalEventId === undefined
    ? {}
    : { causalEventId: oldToNew.get(event.causalEventId) ?? event.causalEventId })
}));
const initialUrl = normalized[0]!.url;
const events: RawCaptureEvent[] = [{
  id: "event-fixture-000",
  sequence: 1,
  elapsedMs: 0,
  capturedAtUtc: "2026-08-17T18:00:00Z",
  tabId: "tab-fixture",
  frameId: "frame-fixture-main",
  url: initialUrl,
  type: "navigation",
  trusted: true,
  navigationKind: "traditional"
}, ...normalized];
const checkpoint: RecorderCheckpoint = {
  schemaVersion: 1,
  sessionId: "session-recorder-e2e-fixture",
  name: "Recorder E2E Fixture",
  state: "completed",
  startedAtUtc: "2026-08-17T18:00:00Z",
  completedAtUtc: "2026-08-17T18:01:00Z",
  timezone: "America/Sao_Paulo",
  locale: "pt-BR",
  origin: new URL(initialUrl).origin,
  options: { captureScreenshots: false, captureSecrets: false, includeUploads: false },
  nextSequence: events.length + 1,
  events,
  resolvedIssueIds: [],
  acceptedPrivacyNotices: ["recorder-privacy-v1"],
  lastCheckpointAtUtc: "2026-08-17T18:01:00Z"
};
const generated = generatePackage(checkpoint.name, checkpoint.events);
assertFinalizable(generated);
await validateGeneratedPackage(generated);
const bundle = await buildBundle({
  bundleId: "bundle-recorder-e2e-fixture",
  createdAtUtc: "2026-08-17T18:01:00Z",
  checkpoint,
  generated,
  evidence: [],
  secrets: [],
  comments: []
});
await verifyBundleIntegrity(bundle.bytes);
await writeFile(outputPath, bundle.bytes);
console.log(JSON.stringify({
  actions: generated.flow.actions.length,
  locators: generated.locators.locators.length,
  capturedEvents: captured.length
}));
