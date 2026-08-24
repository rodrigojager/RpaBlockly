import type { Candidate, Fingerprint, Locator, RecorderIssue } from "../../../../schemas/generated/contracts.js";
import { createIssue } from "../core/issues.js";
import { sanitizeAttributes, sanitizeText, stableId } from "../core/stable.js";
import type { AuthoredLocator, CandidateObservation, NormalizedIntent, RawCaptureEvent } from "../core/types.js";
import { locatorIdFor } from "../capture/normalizer.js";

const strategyOrder: ReadonlyArray<CandidateObservation["key"]> = [
  "testId", "role", "label", "stableAttribute", "placeholder", "text",
  "stableId", "shortCss", "structuralCss", "xpath"
];

export interface LocatorAuthorshipResult {
  locators: Locator[];
  issues: RecorderIssue[];
  authored: AuthoredLocator[];
}

export function authorLocators(
  events: ReadonlyArray<RawCaptureEvent>,
  intents: ReadonlyArray<NormalizedIntent>
): LocatorAuthorshipResult {
  const requiredIds = new Set(intents.flatMap((intent) =>
    [intent.locatorId, intent.readyLocatorId].filter((id): id is string => id !== undefined)));
  const authored: AuthoredLocator[] = [];
  const issues: RecorderIssue[] = [];
  for (const locatorId of [...requiredIds].sort()) {
    const event = events.find((candidate) =>
      candidate.target !== undefined && locatorIdFor(candidate) === locatorId);
    if (event?.target === undefined) continue;
    const result = authorLocator(event);
    if (result === undefined) {
      issues.push(createIssue(
        "AMBIGUOUS_TARGET",
        "blocking",
        "Nenhum localizador único foi encontrado",
        "Candidatos ambíguos permanecem apenas no diagnóstico e não serão executados.",
        { eventId: event.id, omittedFromFlow: true }
      ));
      continue;
    }
    authored.push(result);
  }
  return {
    authored,
    issues,
    locators: authored.map((item) => ({
      id: item.locatorId,
      displayName: displayNameFor(item.fingerprint),
      candidates: item.candidates,
      fingerprints: [item.fingerprint]
    }))
  };
}

export function authorLocator(event: RawCaptureEvent): AuthoredLocator | undefined {
  const snapshot = event.target;
  if (snapshot === undefined || snapshot.closedShadowRoot) return undefined;
  const ordered = [...snapshot.candidates]
    .sort((left, right) => strategyOrder.indexOf(left.key) - strategyOrder.indexOf(right.key));
  const executable = ordered.filter((candidate) =>
    candidate.matchCount === 1 && candidate.matchesTarget && !candidate.sensitive && !candidate.dynamic);
  if (executable.length === 0) return undefined;
  const locatorId = locatorIdFor(event);
  const candidates: Candidate[] = executable.map((observation, index) => ({
    id: stableId("candidate", locatorId, index, observation.key, observation.expression),
    origin: "recorder",
    recorderRole: index === 0 ? "capturedPrimary" : "capturedAlternative",
    originalOrder: index,
    recipe: {
      ...(snapshot.frames.length === 0 ? {} : { frames: snapshot.frames }),
      ...(snapshot.scope === undefined ? {} : { scope: snapshot.scope }),
      target: observation.expression
    }
  }));
  const fingerprintRole = sanitizeText(snapshot.role, 100);
  const fingerprintName = sanitizeText(snapshot.accessibleName, 300);
  const fingerprintText = sanitizeText(snapshot.text);
  const fingerprint: Fingerprint = {
    id: stableId("fingerprint", locatorId),
    tagName: snapshot.tagName.toLowerCase(),
    ...(fingerprintRole === undefined ? {} : { role: fingerprintRole }),
    ...(fingerprintName === undefined ? {} : { accessibleName: fingerprintName }),
    ...(fingerprintText === undefined ? {} : { text: fingerprintText }),
    attributes: sanitizeAttributes(snapshot.attributes),
    ancestors: snapshot.ancestors.slice(0, 32).map(sanitizeNode),
    previousSiblings: snapshot.previousSiblings.slice(0, 10).map(sanitizeNode),
    nextSiblings: snapshot.nextSiblings.slice(0, 10).map(sanitizeNode)
  };
  return {
    locatorId,
    candidates,
    fingerprint,
    diagnostics: ordered.filter((item) => !executable.includes(item) && !item.sensitive)
  };
}

export function isDynamicToken(value: string): boolean {
  return /(?:^|[-_])(?:ember|react|vue|generated|random)[-_]?\d+/iu.test(value) ||
    /^[0-9a-f]{8}-[0-9a-f-]{27,}$/iu.test(value) ||
    /\d{6,}/u.test(value) || value.length > 100;
}

function sanitizeNode(node: { tagName: string; role?: string; text?: string; attributes: Record<string, string> }) {
  const role = sanitizeText(node.role, 100);
  const text = sanitizeText(node.text, 300);
  return {
    tagName: node.tagName.toLowerCase(),
    ...(role === undefined ? {} : { role }),
    ...(text === undefined ? {} : { text }),
    attributes: sanitizeAttributes(node.attributes)
  };
}

function displayNameFor(fingerprint: Fingerprint): string {
  return (fingerprint.accessibleName ?? fingerprint.text ?? fingerprint.attributes?.name ?? fingerprint.tagName)
    .slice(0, 200);
}
