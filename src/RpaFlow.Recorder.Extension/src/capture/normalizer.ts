import type { RecorderIssue } from "../../../../schemas/generated/contracts.js";
import { createIssue } from "../core/issues.js";
import { sanitizeUrl, stableId } from "../core/stable.js";
import type {
  CapturableActionType,
  NormalizationResult,
  NormalizedIntent,
  RawCaptureEvent
} from "../core/types.js";

const semanticKeys = new Set(["Enter", "Tab", "Escape", "ArrowDown", "ArrowUp", "Space"]);

export function normalizeEvents(source: ReadonlyArray<RawCaptureEvent>): NormalizationResult {
  const events = [...source]
    .filter((event) => event.trusted)
    .sort((left, right) => left.sequence - right.sequence || left.id.localeCompare(right.id));
  const seen = new Set<string>();
  const intents: NormalizedIntent[] = [];
  const issues: RecorderIssue[] = [];

  for (const event of events) {
    if (seen.has(event.id)) continue;
    seen.add(event.id);
    const sanitized = sanitizeUrl(event.url);
    if (sanitized.removedSensitiveQuery) {
      issues.push(createIssue(
        "NAVIGATION_WITH_UNSAFE_QUERY",
        "warning",
        "Parâmetros sensíveis foram removidos da URL",
        "A URL foi sanitizada antes de entrar na gravação.",
        { eventId: event.id }
      ));
    }
    const normalizedEvent = { ...event, url: sanitized.url };
    switch (event.type) {
      case "input":
      case "change":
      case "select":
        normalizeValueEvent(normalizedEvent, intents, issues);
        break;
      case "click":
        appendTargetIntent(normalizedEvent, "click", "Clicar", intents, issues);
        break;
      case "submit":
        if (!hasRecentSubmitClick(normalizedEvent, events)) {
          appendTargetIntent(normalizedEvent, "click", "Enviar formulário", intents, issues);
        }
        break;
      case "keydown":
        if (event.key !== undefined && semanticKeys.has(event.key)) {
          appendTargetIntent(normalizedEvent, "pressKey", `Pressionar ${event.key}`, intents, issues, event.key);
        }
        break;
      case "navigation":
        if (event.causalEventId === undefined) {
          appendIntent(normalizedEvent, "navigate", "Navegar", intents, { value: sanitized.url });
        }
        break;
      case "popup":
      case "tab":
        associatePopup(normalizedEvent, events, intents, issues);
        break;
      case "upload":
        if (event.upload === undefined) {
          issues.push(unsupportedIssue(event, "Upload capturado sem metadados do arquivo."));
        } else {
          appendTargetIntent(normalizedEvent, "upload", "Selecionar arquivo", intents, issues);
        }
        break;
      case "unsupported":
        issues.push(unsupportedIssue(event, event.unsupportedReason ?? "Interação não suportada."));
        break;
    }
  }

  return {
    intents: intents.sort((left, right) => left.sequence - right.sequence || left.id.localeCompare(right.id)),
    issues: deduplicateIssues(issues)
  };
}

export function locatorIdFor(event: RawCaptureEvent): string {
  return stableId("locator", event.tabId, event.frameId, event.targetKey ?? event.id);
}

function normalizeValueEvent(
  event: RawCaptureEvent,
  intents: NormalizedIntent[],
  issues: RecorderIssue[]
): void {
  if (event.secretReference !== undefined) {
    appendTargetIntent(event, "fill", "Preencher campo protegido", intents, issues, undefined, "secret");
    return;
  }
  if (event.value === undefined) {
    issues.push(createIssue(
      "SECRET_NOT_CAPTURED",
      "blocking",
      "Valor sensível não foi capturado",
      "A captura de segredos estava desligada; o passo foi omitido até resolução explícita.",
      { eventId: event.id, omittedFromFlow: true }
    ));
    return;
  }
  const type: CapturableActionType = event.type === "select"
    ? "selectOption"
    : typeof event.value === "boolean" ? "setChecked" : "fill";
  const prior = [...intents].reverse().find((intent) =>
    intent.locatorId === locatorIdFor(event) &&
    intent.type === type &&
    event.elapsedMs - intent.elapsedMs <= 2_500);
  if (prior !== undefined) {
    prior.value = event.value;
    prior.eventIds = [...new Set([...prior.eventIds, event.id])];
    prior.elapsedMs = event.elapsedMs;
    return;
  }
  appendTargetIntent(event, type, actionName(type), intents, issues, event.value, "input");
}

function appendTargetIntent(
  event: RawCaptureEvent,
  type: CapturableActionType,
  name: string,
  intents: NormalizedIntent[],
  issues: RecorderIssue[],
  value?: string | boolean,
  sourceKind?: "input" | "secret" | "attachment"
): void {
  if (event.target === undefined || event.target.closedShadowRoot || event.target.inaccessibleFrame) {
    issues.push(createIssue(
      event.target?.closedShadowRoot === true
        ? "UNSUPPORTED_CLOSED_SHADOW_ROOT"
        : event.target?.inaccessibleFrame === true
          ? "CROSS_ORIGIN_FRAME_NOT_CAPTURED"
        : "AMBIGUOUS_TARGET",
      "blocking",
      event.target?.closedShadowRoot === true
        ? "Shadow root fechado não pode ser gravado"
        : event.target?.inaccessibleFrame === true
          ? "A cadeia do iframe não pôde ser validada"
        : "Alvo da interação não foi identificado",
      "O passo foi omitido porque não há receita executável e única para o alvo.",
      { eventId: event.id, omittedFromFlow: true }
    ));
    return;
  }
  appendIntent(event, type, name, intents, {
    locatorId: locatorIdFor(event),
    ...(value === undefined ? {} : { value }),
    ...(sourceKind === undefined ? {} : { valueSourceKind: sourceKind }),
    ...(event.secretReference === undefined ? {} : { secretReference: event.secretReference }),
    ...(event.upload === undefined ? {} : { upload: event.upload })
  });
}

function appendIntent(
  event: RawCaptureEvent,
  type: CapturableActionType,
  name: string,
  intents: NormalizedIntent[],
  details: Partial<NormalizedIntent>
): void {
  const actionId = stableId("action", event.id, type);
  intents.push({
    id: stableId("intent", event.id, type),
    actionId,
    type,
    name,
    sequence: event.sequence,
    elapsedMs: event.elapsedMs,
    eventIds: [event.id],
    tabId: event.tabId,
    frameId: event.frameId,
    url: event.url,
    ...details
  });
}

function associatePopup(
  popup: RawCaptureEvent,
  events: RawCaptureEvent[],
  intents: NormalizedIntent[],
  issues: RecorderIssue[]
): void {
  const causal = popup.causalEventId === undefined
    ? undefined
    : intents.find((intent) => intent.eventIds.includes(popup.causalEventId!));
  const readyEvent = events.find((candidate) =>
    candidate.sequence > popup.sequence && candidate.tabId === popup.tabId && candidate.target !== undefined);
  if (causal?.type !== "click" || readyEvent === undefined) {
    issues.push(createIssue(
      "POPUP_RELATION_UNCERTAIN",
      "warning",
      "Relação com popup ou aba precisa de revisão",
      "Não foi possível provar simultaneamente o clique causal e um alvo pronto na nova página.",
      { eventId: popup.id, ...(causal === undefined ? {} : { actionId: causal.actionId }) }
    ));
    return;
  }
  causal.type = "clickAndSwitchPage";
  causal.name = "Clicar e aguardar nova página";
  causal.readyLocatorId = locatorIdFor(readyEvent);
  causal.eventIds.push(popup.id);
}

function hasRecentSubmitClick(submit: RawCaptureEvent, events: RawCaptureEvent[]): boolean {
  return events.some((candidate) => candidate.type === "click" &&
    candidate.sequence < submit.sequence && submit.elapsedMs - candidate.elapsedMs <= 1_000 &&
    candidate.formKey !== undefined && candidate.formKey === submit.formKey);
}

function unsupportedIssue(event: RawCaptureEvent, detail: string): RecorderIssue {
  return createIssue(
    event.unsupportedCode ?? "UNSUPPORTED_INTERACTION",
    "blocking",
    "Interação não suportada pelo catálogo V2",
    detail,
    { eventId: event.id, omittedFromFlow: true }
  );
}

function actionName(type: CapturableActionType): string {
  switch (type) {
    case "fill": return "Preencher campo";
    case "selectOption": return "Selecionar opção";
    case "setChecked": return "Alterar marcação";
    default: return type;
  }
}

function deduplicateIssues(issues: RecorderIssue[]): RecorderIssue[] {
  return [...new Map(issues.map((issue) => [issue.id, issue])).values()]
    .sort((left, right) => left.id.localeCompare(right.id));
}
