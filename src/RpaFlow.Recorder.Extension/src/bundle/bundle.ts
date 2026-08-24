import { strFromU8, strToU8, unzipSync, zipSync, type Zippable } from "fflate";
import type {
  RpaBlocklyRecorderBundleV1,
  RpaBlocklyRecorderEvidenceV1,
  RpaBlocklyRecorderIntegrityV1,
  RpaBlocklyRecorderIssuesV1,
  RpaBlocklyRecorderSessionV1
} from "../../../../schemas/generated/contracts.js";
import { canonicalJson, sha256Hex } from "../core/stable.js";
import type {
  EncryptedSecretEnvelope,
  RecorderCheckpoint,
  UploadCapture
} from "../core/types.js";
import type { EvidenceAsset } from "../evidence/evidence.js";
import type { GeneratedPackage } from "../package/generator.js";

export interface BundleComment {
  id: string;
  actionId?: string;
  text: string;
}

export interface BundleBuildInput {
  bundleId: string;
  createdAtUtc: string;
  checkpoint: RecorderCheckpoint;
  generated: GeneratedPackage;
  evidence: EvidenceAsset[];
  secrets: EncryptedSecretEnvelope[];
  comments: BundleComment[];
}

export interface BuiltBundle {
  bytes: Uint8Array;
  manifest: RpaBlocklyRecorderBundleV1;
  integrity: RpaBlocklyRecorderIntegrityV1;
  entries: ReadonlyMap<string, Uint8Array>;
}

const fixedZipDate = new Date(1980, 0, 1, 0, 0, 0, 0);
const encoder = new TextEncoder();
const requiredPackagePaths = [
  "package/flow.production.json",
  "package/locators.production.json",
  "package/rpa.policy.json"
];

export async function buildBundle(input: BundleBuildInput): Promise<BuiltBundle> {
  const entries = new Map<string, Uint8Array>();
  addJson(entries, "package/flow.production.json", input.generated.flow);
  addJson(entries, "package/locators.production.json", input.generated.locators);
  addJson(entries, "package/rpa.policy.json", input.generated.policy);
  addJson(entries, "samples/inputs.sample.json", input.generated.samples);
  addJson(entries, "recording/session.json", createSessionDocument(input));
  addJson(entries, "recording/events.json", {
    schemaVersion: 1,
    events: input.checkpoint.events.map(stripPrivateEventData)
  });
  addJson(entries, "recording/issues.json", {
    schemaVersion: 1,
    issues: input.generated.issues
  } satisfies RpaBlocklyRecorderIssuesV1);
  if (input.comments.length > 0) addJson(entries, "recording/comments.json", {
    schemaVersion: 1,
    comments: input.comments
  });

  if (input.evidence.length > 0) {
    const evidenceDocument: RpaBlocklyRecorderEvidenceV1 = {
      schemaVersion: 1,
      items: input.evidence.map((asset) => asset.metadata)
    };
    addJson(entries, "evidence/index.json", evidenceDocument);
    for (const asset of input.evidence) {
      addBinary(entries, asset.metadata.path, asset.image);
      addBinary(entries, asset.metadata.thumbnailPath, asset.thumbnail);
    }
  }
  if (input.secrets.length > 0) {
    addJson(entries, "secrets/index.json", {
      schemaVersion: 1,
      algorithm: "AES-256-GCM+RSA-OAEP-SHA-256",
      items: input.secrets.map((secret) => secret.reference).sort()
    });
    for (const secret of input.secrets) {
      addJson(entries, `secrets/${safeReferenceName(secret.reference)}.json`, secret);
    }
  }
  addUploads(entries, input.checkpoint.events.map((event) => event.upload).filter(isUpload));
  enforceEntryLimits(entries);

  const payloadFiles = [...entries.keys()].sort();
  const unresolved = input.generated.issues.filter((issue) => !issue.resolved);
  const manifest: RpaBlocklyRecorderBundleV1 = {
    bundleFormat: "rpablockly-recorder",
    bundleVersion: 1,
    bundleId: input.bundleId,
    createdAtUtc: input.createdAtUtc,
    recorderVersion: "1.0.0-rc.6",
    generatorVersion: "1.0.0-rc.6",
    rpaPackageRoot: "package",
    schemas: {
      flow: 2, locators: 1, policy: 1, session: 1, evidence: 1, issues: 1, integrity: 1
    },
    displayName: input.checkpoint.name,
    origin: "chrome-recorder",
    ...(input.checkpoint.options.recipientKeyId === undefined
      ? {}
      : { recipientKeyId: input.checkpoint.options.recipientKeyId }),
    hasSecrets: input.secrets.length > 0,
    hasUploads: input.checkpoint.events.some((event) => event.upload !== undefined),
    stepCount: input.generated.flow.actions.length,
    blockingIssueCount: unresolved.filter((issue) => issue.severity === "blocking").length,
    warningIssueCount: unresolved.filter((issue) => issue.severity === "warning").length,
    files: payloadFiles,
    containsReplay: false
  };
  addJson(entries, "manifest.json", manifest);
  const integrity: RpaBlocklyRecorderIntegrityV1 = {
    schemaVersion: 1,
    entries: await Promise.all([...entries.entries()].sort(([left], [right]) => left.localeCompare(right))
      .map(async ([path, bytes]) => ({ path, sha256: await sha256Hex(bytes), size: bytes.length })))
  };
  addJson(entries, "integrity.json", integrity);

  const zippable: Zippable = {};
  for (const [path, bytes] of [...entries.entries()].sort(([left], [right]) => left.localeCompare(right))) {
    zippable[path] = [bytes, { mtime: fixedZipDate }];
  }
  return { bytes: zipSync(zippable, { level: 6 }), manifest, integrity, entries };
}

export async function verifyBundleIntegrity(bytes: Uint8Array): Promise<void> {
  const unzipped = unzipSync(bytes);
  const integrityBytes = unzipped["integrity.json"];
  if (integrityBytes === undefined) throw new Error("Bundle sem integrity.json.");
  const integrity = JSON.parse(strFromU8(integrityBytes)) as RpaBlocklyRecorderIntegrityV1;
  const seen = new Set<string>();
  for (const entry of integrity.entries) {
    const folded = entry.path.toLowerCase();
    if (seen.has(folded)) throw new Error("Integridade contém caminho duplicado.");
    seen.add(folded);
    const content = unzipped[entry.path];
    if (content === undefined || content.length !== entry.size || await sha256Hex(content) !== entry.sha256) {
      throw new Error(`Integridade inválida para ${entry.path}.`);
    }
  }
}

function createSessionDocument(input: BundleBuildInput): RpaBlocklyRecorderSessionV1 {
  const tabs = [...input.checkpoint.events.reduce((result, event) => {
    if (!result.has(event.tabId)) {
      result.set(event.tabId, { id: event.tabId, initialUrl: event.url });
    }
    return result;
  }, new Map<string, { id: string; initialUrl: string }>()).values()];
  const frames = [...new Map(input.checkpoint.events.map((event) => [event.frameId, {
    id: event.frameId,
    tabId: event.tabId,
    url: event.url,
    accessible: true
  }])).values()];
  const evidenceByAction = new Map(input.evidence.map((asset) => [asset.metadata.actionId, asset.metadata.id]));
  return {
    schemaVersion: 1,
    sessionId: input.checkpoint.sessionId,
    name: input.checkpoint.name,
    state: "completed",
    startedAtUtc: input.checkpoint.startedAtUtc,
    completedAtUtc: input.createdAtUtc,
    timezone: input.checkpoint.timezone,
    locale: input.checkpoint.locale,
    options: {
      captureScreenshots: input.checkpoint.options.captureScreenshots,
      captureSecrets: input.checkpoint.options.captureSecrets,
      includeUploads: input.checkpoint.options.includeUploads
    },
    origins: sessionOrigins(input.checkpoint),
    tabs,
    frames,
    eventCount: input.checkpoint.events.length,
    associations: input.generated.intents.map((intent) => ({
      eventId: intent.eventIds[0] ?? intent.id,
      actionId: intent.actionId,
      ...(intent.locatorId === undefined ? {} : { locatorId: intent.locatorId }),
      ...(evidenceByAction.has(intent.actionId)
        ? { evidenceId: evidenceByAction.get(intent.actionId)! }
        : {})
    })),
    acceptedPrivacyNotices: input.checkpoint.acceptedPrivacyNotices
  };
}

function sessionOrigins(checkpoint: RecorderCheckpoint): string[] {
  const origins = new Set<string>([checkpoint.origin]);
  for (const event of checkpoint.events) {
    try {
      const url = new URL(event.url);
      if (/^https?:$/u.test(url.protocol)) origins.add(url.origin);
    } catch {
      // A URL já será rejeitada pelo validador correspondente quando for inválida.
    }
  }
  return [...origins];
}

function stripPrivateEventData(event: RecorderCheckpoint["events"][number]) {
  const { value: _value, upload, ...safe } = event;
  return {
    ...safe,
    ...(safe.target === undefined ? {} : {
      target: {
        ...safe.target,
        candidates: safe.target.candidates.filter((candidate) => !candidate.sensitive)
      }
    }),
    ...(upload === undefined ? {} : {
      upload: {
        name: upload.name,
        mimeType: upload.mimeType,
        size: upload.size,
        ...(upload.sha256 === undefined ? {} : { sha256: upload.sha256 }),
        included: upload.included
      }
    })
  };
}

function addUploads(entries: Map<string, Uint8Array>, uploads: UploadCapture[]): void {
  if (uploads.length === 0) return;
  addJson(entries, "recording/uploads.json", {
    schemaVersion: 1,
    items: uploads.map(({ contentBase64: _content, ...metadata }) => metadata)
  });
  for (const upload of uploads.filter((item) => item.included && item.contentBase64 !== undefined)) {
    addBinary(entries, `samples/uploads/${upload.name}`, base64ToBytes(upload.contentBase64!));
  }
}

function addJson(entries: Map<string, Uint8Array>, path: string, value: unknown): void {
  addBinary(entries, path, encoder.encode(canonicalJson(value)));
}

function addBinary(entries: Map<string, Uint8Array>, path: string, bytes: Uint8Array): void {
  validatePath(path);
  if (entries.has(path)) throw new Error(`Entrada duplicada: ${path}.`);
  entries.set(path, bytes);
}

function enforceEntryLimits(entries: Map<string, Uint8Array>): void {
  if (entries.size > 500) throw new Error("O bundle excede 500 entradas.");
  const total = [...entries.values()].reduce((sum, bytes) => sum + bytes.length, 0);
  if (total > 100 * 1024 * 1024) throw new Error("O bundle excede 100 MiB descompactados.");
  if ([...entries.values()].some((bytes) => bytes.length > 25 * 1024 * 1024)) {
    throw new Error("Uma entrada do bundle excede 25 MiB.");
  }
  for (const required of requiredPackagePaths) {
    if (!entries.has(required)) throw new Error(`Pacote sem ${required}.`);
  }
}

function validatePath(path: string): void {
  if (!/^[A-Za-z0-9][A-Za-z0-9._/-]{0,239}$/u.test(path) ||
      path.includes("\\") || path.split("/").some((part) => part === "" || part === "." || part === "..")) {
    throw new Error(`Caminho inseguro no bundle: ${path}.`);
  }
}

function safeReferenceName(reference: string): string {
  return reference.replace(/^secret\.recorded\./u, "").replace(/[^A-Za-z0-9_-]/gu, "-");
}

function base64ToBytes(value: string): Uint8Array {
  return strToU8(atob(value), true);
}

function isUpload(value: UploadCapture | undefined): value is UploadCapture {
  return value !== undefined;
}
