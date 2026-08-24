import type {
  Action,
  Candidate,
  Expression,
  Fingerprint,
  RpaBlocklyRecorderIssuesV1
} from "../../../../schemas/generated/contracts.js";

export const CAPTURABLE_ACTION_TYPES = [
  "navigate",
  "click",
  "fill",
  "selectOption",
  "setChecked",
  "pressKey",
  "clickAndSwitchPage",
  "upload"
] as const satisfies ReadonlyArray<Action["type"]>;

export type CapturableActionType = typeof CAPTURABLE_ACTION_TYPES[number];
export type RawEventType =
  | "click" | "input" | "change" | "submit" | "keydown" | "select"
  | "navigation" | "tab" | "popup" | "upload" | "unsupported";

export interface CandidateObservation {
  key: "testId" | "role" | "label" | "stableAttribute" | "placeholder" |
    "text" | "stableId" | "shortCss" | "structuralCss" | "xpath";
  expression: Expression;
  matchCount: number;
  matchesTarget: boolean;
  sensitive: boolean;
  dynamic: boolean;
}

export interface ElementRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface ElementSnapshot {
  tagName: string;
  role?: string;
  accessibleName?: string;
  text?: string;
  attributes: Record<string, string>;
  ancestors: Array<FingerprintNodeSnapshot>;
  previousSiblings: Array<FingerprintNodeSnapshot>;
  nextSiblings: Array<FingerprintNodeSnapshot>;
  candidates: Array<CandidateObservation>;
  frames: Array<Expression>;
  scope?: Expression;
  closedShadowRoot: boolean;
  inaccessibleFrame: boolean;
  rect?: ElementRect;
}

export interface FingerprintNodeSnapshot {
  tagName: string;
  role?: string;
  text?: string;
  attributes: Record<string, string>;
}

export interface UploadCapture {
  name: string;
  mimeType: string;
  size: number;
  sha256?: string;
  included: boolean;
  contentBase64?: string;
}

export interface RawCaptureEvent {
  id: string;
  sequence: number;
  elapsedMs: number;
  capturedAtUtc: string;
  tabId: string;
  frameId: string;
  url: string;
  type: RawEventType;
  trusted: boolean;
  causalEventId?: string;
  target?: ElementSnapshot;
  targetKey?: string;
  formKey?: string;
  value?: string | boolean;
  secretReference?: string;
  key?: string;
  navigationKind?: "traditional" | "spa" | "tab" | "popup";
  upload?: UploadCapture;
  unsupportedReason?: string;
  unsupportedCode?: RpaBlocklyRecorderIssuesV1["issues"][number]["code"];
}

export interface NormalizedIntent {
  id: string;
  actionId: string;
  type: CapturableActionType;
  name: string;
  sequence: number;
  elapsedMs: number;
  eventIds: string[];
  tabId: string;
  frameId: string;
  url: string;
  locatorId?: string;
  readyLocatorId?: string;
  value?: string | boolean;
  valueSourceKind?: "input" | "secret" | "attachment";
  secretReference?: string;
  upload?: UploadCapture;
}

export interface AuthoredLocator {
  locatorId: string;
  candidates: Candidate[];
  fingerprint: Fingerprint;
  diagnostics: CandidateObservation[];
}

export interface NormalizationResult {
  intents: NormalizedIntent[];
  issues: RpaBlocklyRecorderIssuesV1["issues"];
}

export interface RecorderOptions {
  captureScreenshots: boolean;
  captureSecrets: boolean;
  includeUploads: boolean;
  recipientKeyId?: string;
  recipientPublicKeyPem?: string;
}

export type EvidenceCaptureFailureStage =
  | "prepare"
  | "capture"
  | "process"
  | "store";

export interface EvidenceCaptureStatus {
  attempted: number;
  captured: number;
  skipped: number;
  failed: number;
  lastFailure?: {
    eventId: string;
    stage: EvidenceCaptureFailureStage;
    message: string;
    occurredAtUtc: string;
  };
}

export interface EncryptedSecretEnvelope {
  schemaVersion: 1;
  reference: string;
  keyId: string;
  algorithm: "AES-256-GCM+RSA-OAEP-SHA-256";
  iv: string;
  aad: string;
  ciphertext: string;
  wrappedKey: string;
}

export interface RecorderCheckpoint {
  schemaVersion: 1;
  sessionId: string;
  name: string;
  state: RecorderState;
  startedAtUtc: string;
  completedAtUtc?: string;
  timezone: string;
  locale: string;
  origin: string;
  options: RecorderOptions;
  evidenceCapture?: EvidenceCaptureStatus;
  nextSequence: number;
  events: RawCaptureEvent[];
  resolvedIssueIds: string[];
  acceptedPrivacyNotices: string[];
  lastCheckpointAtUtc: string;
}

export type RecorderState =
  | "idle" | "recording" | "paused" | "finalizing" | "completed" | "failed";

export interface RecorderClock {
  now(): Date;
}

export interface SessionStorageAdapter {
  get<T>(key: string): Promise<T | undefined>;
  set<T>(key: string, value: T): Promise<void>;
  remove(key: string): Promise<void>;
}
