import type { RawCaptureEvent, RecorderCheckpoint, RecorderOptions } from "../core/types.js";

export type RecorderRequest =
  | { type: "RECORDER_GET_STATE" }
  | { type: "RECORDER_GET_TARGET" }
  | {
      type: "RECORDER_START";
      name: string;
      tabId: number;
      origin: string;
      options: RecorderOptions;
    }
  | { type: "RECORDER_PAUSE" }
  | { type: "RECORDER_RESUME" }
  | { type: "RECORDER_RECONNECT" }
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
  | {
      ok: true;
      checkpoint?: RecorderCheckpoint;
      target?: RecorderTarget;
      accessNotice?: RecorderAccessNotice;
    }
  | { ok: false; error: string };

export interface RecorderTarget {
  tabId: number;
  windowId: number;
  url: string;
  origin: string;
}

export interface RecorderAccessNotice extends RecorderTarget {
  kind: "originReconnect";
  requestedAtUtc: string;
}

export interface RecorderUiRefresh {
  type: "RPABLOCKLY_RECORDER_REFRESH";
}

export function isRecorderRequest(value: unknown): value is RecorderRequest {
  return value !== null && typeof value === "object" &&
    typeof (value as { type?: unknown }).type === "string" &&
    (value as { type: string }).type.startsWith("RECORDER_");
}

export function isRecorderUiRefresh(value: unknown): value is RecorderUiRefresh {
  return value !== null && typeof value === "object" &&
    (value as { type?: unknown }).type === "RPABLOCKLY_RECORDER_REFRESH";
}
