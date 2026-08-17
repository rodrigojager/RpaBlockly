import type { RecorderCheckpoint, RecorderClock, RecorderOptions, RecorderState } from "./types.js";
import { sanitizeUrl, slug, stableId } from "./stable.js";

const transitions: Readonly<Record<RecorderState, ReadonlyArray<RecorderState>>> = {
  idle: ["recording"],
  recording: ["paused", "finalizing", "failed"],
  paused: ["recording", "finalizing", "failed"],
  finalizing: ["completed", "failed", "paused"],
  completed: [],
  failed: []
};

export function createCheckpoint(
  name: string,
  origin: string,
  options: RecorderOptions,
  clock: RecorderClock = { now: () => new Date() }
): RecorderCheckpoint {
  const now = clock.now();
  const safeOrigin = new URL(sanitizeUrl(origin).url).origin;
  const sessionId = stableId("session", slug(name), safeOrigin, now.toISOString());
  return {
    schemaVersion: 1,
    sessionId,
    name: name.trim().slice(0, 200),
    state: "idle",
    startedAtUtc: now.toISOString(),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
    locale: navigatorLocale(),
    origin: safeOrigin,
    options: { ...options },
    nextSequence: 1,
    events: [],
    resolvedIssueIds: [],
    acceptedPrivacyNotices: [],
    lastCheckpointAtUtc: now.toISOString()
  };
}

export function transition(
  checkpoint: RecorderCheckpoint,
  next: RecorderState,
  clock: RecorderClock = { now: () => new Date() }
): RecorderCheckpoint {
  if (!transitions[checkpoint.state].includes(next)) {
    throw new Error(`Transição inválida do Recorder: ${checkpoint.state} → ${next}.`);
  }
  const now = clock.now().toISOString();
  return {
    ...checkpoint,
    state: next,
    ...(next === "completed" || next === "failed" ? { completedAtUtc: now } : {}),
    lastCheckpointAtUtc: now
  };
}

export function canCapture(checkpoint: RecorderCheckpoint): boolean {
  return checkpoint.state === "recording";
}

function navigatorLocale(): string {
  return typeof navigator === "undefined" ? "pt-BR" : navigator.language || "pt-BR";
}
