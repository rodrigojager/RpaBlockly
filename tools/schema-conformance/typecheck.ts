import type {
  RpaBlocklyFlowV2,
  RpaBlocklyLocatorCatalogV1,
  RpaBlocklyPolicyV1,
  RpaBlocklyRecorderBundleV1,
  RpaBlocklyRecorderSessionV1,
  RpaBlocklyRecorderEvidenceV1,
  RpaBlocklyRecorderIssuesV1,
  RpaBlocklyRecorderIntegrityV1
} from "../../schemas/generated/contracts.js";

const flow: RpaBlocklyFlowV2 = {
  schemaVersion: 2,
  name: "Conformidade TypeScript",
  actions: [
    {
      id: "wait",
      type: "wait",
      name: "Aguardar",
      target: { locatorId: "ready", cardinality: "single" }
    }
  ],
  subflows: {}
};

const locators: RpaBlocklyLocatorCatalogV1 = {
  schemaVersion: 1,
  locators: []
};

const policy: RpaBlocklyPolicyV1 = {
  schemaVersion: 1,
  locatorResilience: {
    mode: "strict",
    learningWriteBack: "disabled",
    promotion: "disabled",
    failedPrimary: "keep",
    minimumConfidence: 0.85,
    minimumRunnerUpGap: 0.1,
    maximumCandidatesPerLocator: 20,
    maximumHeuristicNodes: 5000,
    maximumResolutionMilliseconds: 30000
  }
};

const manifest: RpaBlocklyRecorderBundleV1 = {
  bundleFormat: "rpablockly-recorder",
  bundleVersion: 1,
  bundleId: "bundle-typecheck",
  createdAtUtc: "2026-08-17T18:00:00Z",
  recorderVersion: "1.0.0",
  generatorVersion: "1.0.0",
  rpaPackageRoot: "package",
  schemas: {
    flow: 2, locators: 1, policy: 1, session: 1, evidence: 1, issues: 1, integrity: 1
  },
  displayName: "Typecheck",
  origin: "chrome-recorder",
  hasSecrets: false,
  hasUploads: false,
  stepCount: 0,
  blockingIssueCount: 0,
  warningIssueCount: 0,
  files: ["package/flow.production.json"],
  containsReplay: false
};
const session: RpaBlocklyRecorderSessionV1 = {
  schemaVersion: 1,
  sessionId: "session-typecheck",
  name: "Typecheck",
  state: "completed",
  startedAtUtc: "2026-08-17T18:00:00Z",
  timezone: "UTC",
  locale: "pt-BR",
  options: { captureScreenshots: false, captureSecrets: false, includeUploads: false },
  origins: [], tabs: [], frames: [], eventCount: 0, associations: [],
  acceptedPrivacyNotices: []
};
const evidence: RpaBlocklyRecorderEvidenceV1 = { schemaVersion: 1, items: [] };
const issues: RpaBlocklyRecorderIssuesV1 = { schemaVersion: 1, issues: [] };
const integrity: RpaBlocklyRecorderIntegrityV1 = {
  schemaVersion: 1,
  entries: [{ path: "package/flow.production.json", sha256: "A".repeat(64), size: 1 }]
};

void [flow, locators, policy, manifest, session, evidence, issues, integrity];
