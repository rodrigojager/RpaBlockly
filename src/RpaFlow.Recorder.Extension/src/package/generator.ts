import type {
  Action,
  InputRequirement,
  RpaBlocklyFlowV2,
  RpaBlocklyLocatorCatalogV1,
  RpaBlocklyPolicyV1,
  RecorderIssue
} from "../../../../schemas/generated/contracts.js";
import { applyIssueResolutions, createIssue } from "../core/issues.js";
import { slug } from "../core/stable.js";
import type { NormalizedIntent, RawCaptureEvent } from "../core/types.js";
import { authorLocators } from "../locators/authoring.js";
import { normalizeEvents } from "../capture/normalizer.js";

export interface GeneratedPackage {
  flow: RpaBlocklyFlowV2;
  locators: RpaBlocklyLocatorCatalogV1;
  policy: RpaBlocklyPolicyV1;
  samples: { input: Record<string, unknown> };
  issues: RecorderIssue[];
  intents: NormalizedIntent[];
}

export function generatePackage(
  name: string,
  events: ReadonlyArray<RawCaptureEvent>,
  resolvedIssueIds: ReadonlyArray<string> = []
): GeneratedPackage {
  const normalized = normalizeEvents(events);
  const authored = authorLocators(events, normalized.intents);
  const executableLocatorIds = new Set(authored.locators.map((locator) => locator.id));
  const inputs: InputRequirement[] = [];
  const inputRoot: Record<string, unknown> = {};
  const actions: Action[] = [];
  const issues = [...normalized.issues, ...authored.issues];

  normalized.intents.forEach((intent, index) => {
    if ((intent.locatorId !== undefined && !executableLocatorIds.has(intent.locatorId)) ||
        (intent.readyLocatorId !== undefined && !executableLocatorIds.has(intent.readyLocatorId))) {
      issues.push(createIssue(
        "AMBIGUOUS_TARGET",
        "blocking",
        "Ação sem localizador executável",
        "A ação foi omitida porque o alvo principal ou de prontidão não é único.",
        {
          ...(intent.eventIds[0] === undefined ? {} : { eventId: intent.eventIds[0] }),
          actionId: intent.actionId,
          omittedFromFlow: true
        }
      ));
      return;
    }
    actions.push(toAction(intent, index, inputs, inputRoot));
  });

  if (actions.length === 0) {
    issues.push(createIssue(
      "UNSUPPORTED_INTERACTION",
      "blocking",
      "A gravação não possui ações executáveis",
      "Grave ao menos uma interação suportada ou resolva as pendências.",
      { omittedFromFlow: true }
    ));
  }

  const result: GeneratedPackage = {
    flow: {
      schemaVersion: 2,
      name: name.trim().slice(0, 200),
      inputs,
      actions,
      subflows: {}
    },
    locators: { schemaVersion: 1, locators: authored.locators },
    policy: {
      schemaVersion: 1,
      locatorResilience: {
        mode: "strict",
        learningWriteBack: "disabled",
        promotion: "disabled",
        failedPrimary: "keep",
        minimumConfidence: 0.85,
        minimumRunnerUpGap: 0.1,
        maximumCandidatesPerLocator: 20,
        maximumHeuristicNodes: 5_000,
        maximumResolutionMilliseconds: 30_000
      }
    },
    samples: { input: inputRoot },
    issues: applyIssueResolutions(deduplicateIssues(issues), [...resolvedIssueIds]),
    intents: normalized.intents
  };
  return result;
}

export function assertFinalizable(result: GeneratedPackage): void {
  const unresolved = result.issues.filter((issue) => issue.severity === "blocking" && !issue.resolved);
  if (unresolved.length > 0) {
    throw new Error(`A gravação possui ${unresolved.length} pendência(s) bloqueante(s).`);
  }
  if (result.flow.actions.length === 0) throw new Error("A gravação não possui ações executáveis.");
}

function toAction(
  intent: NormalizedIntent,
  index: number,
  inputs: InputRequirement[],
  inputRoot: Record<string, unknown>
): Action {
  const action: Action = {
    id: intent.actionId,
    type: intent.type,
    name: intent.name,
    ...(intent.locatorId === undefined
      ? {}
      : { target: { locatorId: intent.locatorId, cardinality: "single" as const } }),
    ...(intent.readyLocatorId === undefined
      ? {}
      : { ready: { locatorId: intent.readyLocatorId, cardinality: "single" as const } })
  };
  if (intent.type === "selectOption") action.optionMode = "value";
  if (intent.type === "switchPage") {
    action.property = "url";
    action.comparison = "exact";
  }
  if (intent.type === "download") action.downloadMode = "click";
  if (intent.type === "upload" && intent.upload !== undefined) {
    const key = `file_${String(index + 1).padStart(3, "0")}_${slug(intent.upload.name, "arquivo")}`;
    const path = `attachments.recorded.${key}`;
    action.valueSource = path;
    inputs.push({ path, type: "string", required: true });
  } else if (intent.secretReference !== undefined) {
    action.valueSource = intent.secretReference;
  } else if (intent.value !== undefined) {
    const key = `step_${String(index + 1).padStart(3, "0")}_${slug(intent.name)}`;
    const path = `input.recorded.${key}`;
    action.valueSource = path;
    inputs.push({ path, type: typeof intent.value === "boolean" ? "boolean" : "string", required: true });
    inputRoot[key] = intent.value;
  }
  return action;
}

function deduplicateIssues(issues: RecorderIssue[]): RecorderIssue[] {
  return [...new Map(issues.map((issue) => [issue.id, issue])).values()]
    .sort((left, right) => left.id.localeCompare(right.id));
}
