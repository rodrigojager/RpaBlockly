import type { RawCaptureEvent, RecorderCheckpoint, RecorderOptions } from "../core/types.js";

export type RecorderRequest =
  | { type: "RECORDER_GET_STATE" }
  | { type: "RECORDER_START"; name: string; origin: string; options: RecorderOptions }
  | { type: "RECORDER_PAUSE" }
  | { type: "RECORDER_RESUME" }
  | { type: "RECORDER_FINALIZE" }
  | { type: "RECORDER_COMPLETE" }
  | { type: "RECORDER_ABORT_FINALIZE" }
  | { type: "RECORDER_FAIL"; reason: string }
  | { type: "RECORDER_CANCEL" }
  | { type: "RECORDER_RESOLVE_ISSUE"; issueId: string }
  | { type: "RECORDER_CAPTURE_EVENT"; event: RawCaptureEvent; transientSecret?: string }
  | { type: "RECORDER_PREPARE_SCREENSHOT"; eventId: string }
  | { type: "RECORDER_CLEAR_SCREENSHOT"; eventId: string }
  | { type: "RECORDER_CONFIGURE_CONTENT"; options: RecorderOptions };

export type RecorderResponse =
  | { ok: true; checkpoint?: RecorderCheckpoint }
  | { ok: false; error: string };

export function isRecorderRequest(value: unknown): value is RecorderRequest {
  return value !== null && typeof value === "object" &&
    typeof (value as { type?: unknown }).type === "string" &&
    (value as { type: string }).type.startsWith("RECORDER_");
}
