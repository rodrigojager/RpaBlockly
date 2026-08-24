import type { CandidateObservation, ElementSnapshot, RawCaptureEvent, RecorderCheckpoint } from "../src/core/types.js";

export const fixedStart = "2026-08-17T18:00:00.000Z";

export function targetSnapshot(overrides: Partial<ElementSnapshot> = {}): ElementSnapshot {
  const candidate: CandidateObservation = {
    key: "testId",
    expression: { strategy: "testId", text: "customer-name" },
    matchCount: 1,
    matchesTarget: true,
    sensitive: false,
    dynamic: false
  };
  return {
    tagName: "input",
    role: "textbox",
    accessibleName: "Nome",
    attributes: { "data-testid": "customer-name", name: "customerName" },
    ancestors: [{ tagName: "form", attributes: { id: "customer-form" } }],
    previousSiblings: [],
    nextSiblings: [],
    candidates: [candidate],
    frames: [],
    closedShadowRoot: false,
    inaccessibleFrame: false,
    rect: { x: 10, y: 20, width: 200, height: 32 },
    ...overrides
  };
}

export function rawEvent(
  sequence: number,
  type: RawCaptureEvent["type"],
  overrides: { [Key in keyof RawCaptureEvent]?: RawCaptureEvent[Key] | undefined } = {}
): RawCaptureEvent {
  const result = {
    id: `event-${String(sequence).padStart(3, "0")}`,
    sequence,
    elapsedMs: sequence * 100,
    capturedAtUtc: new Date(Date.parse(fixedStart) + sequence * 100).toISOString(),
    tabId: "tab-1",
    frameId: "frame-1-0",
    url: "https://fixture.test/form",
    type,
    trusted: true,
    targetKey: "customer-name",
    target: targetSnapshot(),
    ...overrides
  } as Record<string, unknown>;
  for (const [key, value] of Object.entries(result)) {
    if (value === undefined) delete result[key];
  }
  return result as unknown as RawCaptureEvent;
}

export function checkpoint(events: RawCaptureEvent[]): RecorderCheckpoint {
  return {
    schemaVersion: 1,
    sessionId: "session-fixture-001",
    name: "Fixture Recorder",
    state: "finalizing",
    startedAtUtc: fixedStart,
    completedAtUtc: "2026-08-17T18:01:00.000Z",
    timezone: "America/Sao_Paulo",
    locale: "pt-BR",
    origin: "https://fixture.test",
    options: { captureScreenshots: false, captureSecrets: false, includeUploads: false },
    nextSequence: Math.max(1, ...events.map((event) => event.sequence + 1)),
    events,
    resolvedIssueIds: [],
    acceptedPrivacyNotices: ["recorder-privacy-v1"],
    lastCheckpointAtUtc: "2026-08-17T18:01:00.000Z"
  };
}
