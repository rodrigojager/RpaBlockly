import type { Expression, EvidenceMask } from "../../../../schemas/generated/contracts.js";
import { sanitizeAttributes, sanitizeText, sanitizeUrl, stableId } from "../core/stable.js";
import type {
  CandidateObservation,
  ElementSnapshot,
  FingerprintNodeSnapshot,
  RawCaptureEvent,
  RecorderOptions
} from "../core/types.js";
import { isDynamicToken } from "../locators/authoring.js";
import type { RecorderRequest } from "../shared/messages.js";
import { captureUpload } from "../uploads/uploads.js";

const marker = "__rpaBlocklyRecorderV2";
const globalState = globalThis as typeof globalThis & { [marker]?: boolean };
if (!globalState[marker]) {
  globalState[marker] = true;
  initialize();
}

function initialize(): void {
  let options: RecorderOptions = {
    captureScreenshots: true,
    captureSecrets: false,
    includeUploads: false
  };
  let localSequence = 0;
  let lastTrustedEventId: string | undefined;
  let lastTrustedAt = Number.NEGATIVE_INFINITY;
  const targets = new Map<string, Element>();
  const overlayNodes: HTMLElement[] = [];
  const startedAt = performance.timeOrigin + performance.now();

  const capture = async (domEvent: Event, type: RawCaptureEvent["type"]): Promise<void> => {
    if (!domEvent.isTrusted) return;
    const element = targetElement(domEvent);
    const target = element === undefined ? undefined : snapshotElement(element, domEvent);
    const elapsedMs = Math.max(0, Math.round(performance.timeOrigin + performance.now() - startedAt));
    const targetKey = element === undefined ? undefined : elementKey(element);
    const eventId = stableId("event-page", ++localSequence, type, elapsedMs, targetKey ?? "none");
    if (element !== undefined) targets.set(eventId, element);
    lastTrustedEventId = eventId;
    lastTrustedAt = performance.now();
    const base: RawCaptureEvent = {
      id: eventId,
      sequence: 0,
      elapsedMs,
      capturedAtUtc: new Date().toISOString(),
      tabId: "tab-pending",
      frameId: "frame-pending",
      url: sanitizeUrl(location.href).url,
      type,
      trusted: true,
      ...(target === undefined ? {} : { target }),
      ...(targetKey === undefined ? {} : { targetKey }),
      ...(formFor(element) === undefined ? {} : { formKey: elementKey(formFor(element)!) })
    };

    let transientSecret: string | undefined;
    if ((type === "input" || type === "change") && element instanceof HTMLInputElement) {
      if (isSensitiveField(element)) {
        if (options.captureSecrets) transientSecret = element.value;
      } else {
        base.value = element.type === "checkbox" || element.type === "radio" ? element.checked : element.value;
      }
    } else if (type === "select" && element instanceof HTMLSelectElement) {
      base.value = element.value;
    } else if (type === "keydown" && domEvent instanceof KeyboardEvent) {
      base.key = domEvent.key;
    } else if (type === "upload" && element instanceof HTMLInputElement && element.files?.[0] !== undefined) {
      base.upload = await captureUpload(element.files[0], options.includeUploads);
    }
    const request: RecorderRequest = {
      type: "RECORDER_CAPTURE_EVENT",
      event: base,
      ...(transientSecret === undefined ? {} : { transientSecret })
    };
    await chrome.runtime.sendMessage(request);
    transientSecret = undefined;
  };

  document.addEventListener("click", (event) => void capture(event, "click"), true);
  document.addEventListener("input", (event) => void capture(event, "input"), true);
  document.addEventListener("change", (event) => {
    const element = targetElement(event);
    void capture(event, element instanceof HTMLSelectElement
      ? "select"
      : element instanceof HTMLInputElement && element.type === "file" ? "upload" : "change");
  }, true);
  document.addEventListener("submit", (event) => void capture(event, "submit"), true);
  document.addEventListener("keydown", (event) => void capture(event, "keydown"), true);

  const navigation = (): void => {
    const now = performance.now();
    if (lastTrustedEventId === undefined || now - lastTrustedAt > 2_000) return;
    const elapsedMs = Math.round(performance.timeOrigin + now - startedAt);
    void chrome.runtime.sendMessage({
      type: "RECORDER_CAPTURE_EVENT",
      event: {
        id: stableId("event-page", ++localSequence, "navigation", elapsedMs, location.href),
        sequence: 0,
        elapsedMs,
        capturedAtUtc: new Date().toISOString(),
        tabId: "tab-pending",
        frameId: "frame-pending",
        url: sanitizeUrl(location.href).url,
        type: "navigation",
        trusted: true,
        causalEventId: lastTrustedEventId,
        navigationKind: "spa"
      }
    } satisfies RecorderRequest);
  };
  for (const method of ["pushState", "replaceState"] as const) {
    const original = history[method];
    history[method] = ((data: unknown, unused: string, url?: string | URL | null) => {
      original.call(history, data, unused, url);
      queueMicrotask(navigation);
    }) as typeof history[typeof method];
  }
  addEventListener("popstate", navigation, true);
  addEventListener("hashchange", navigation, true);

  chrome.runtime.onMessage.addListener((message: unknown, _sender, sendResponse) => {
    const request = message as RecorderRequest;
    if (request.type === "RECORDER_CONFIGURE_CONTENT") {
      options = { ...request.options };
      sendResponse({ ok: true });
      return;
    }
    if (request.type === "RECORDER_PREPARE_SCREENSHOT") {
      const masks = prepareScreenshot(targets.get(request.eventId), overlayNodes);
      sendResponse({ ok: true, masks });
      return;
    }
    if (request.type === "RECORDER_CLEAR_SCREENSHOT") {
      clearOverlays(overlayNodes);
      sendResponse({ ok: true });
    }
  });
}

function snapshotElement(element: Element, event: Event): ElementSnapshot {
  const attributes = collectAttributes(element);
  const accessibleName = accessibleNameFor(element);
  const role = roleFor(element);
  const text = sanitizeText(element.textContent ?? undefined, 300);
  const frameContext = frameExpressions();
  return {
    tagName: element.tagName.toLowerCase(),
    ...(role === undefined ? {} : { role }),
    ...(accessibleName === undefined ? {} : { accessibleName }),
    ...(text === undefined ? {} : { text }),
    attributes,
    ancestors: relatedNodes(element.parentElement, "parent"),
    previousSiblings: siblingNodes(element, "previous"),
    nextSiblings: siblingNodes(element, "next"),
    candidates: candidateObservations(element, role, accessibleName, text),
    frames: frameContext.expressions,
    closedShadowRoot: event.composedPath()[0] !== event.target &&
      event.target instanceof Element && event.target.shadowRoot === null,
    inaccessibleFrame: frameContext.inaccessible,
    rect: toRect(element.getBoundingClientRect())
  };
}

function candidateObservations(
  element: Element,
  role: string | undefined,
  accessibleName: string | undefined,
  text: string | undefined
): CandidateObservation[] {
  const result: CandidateObservation[] = [];
  const testId = element.getAttribute("data-testid") ?? element.getAttribute("data-test-id");
  if (testId) add("testId", { strategy: "testId", text: testId }, `[data-testid="${cssString(testId)}"],[data-test-id="${cssString(testId)}"]`);
  if (role && accessibleName) addFiltered("role", { strategy: "role", role, name: accessibleName },
    [...document.querySelectorAll("*")].filter((candidate) => roleFor(candidate) === role && accessibleNameFor(candidate) === accessibleName));
  const label = labelFor(element);
  if (label) addFiltered("label", { strategy: "label", text: label }, elementsByLabel(label));
  for (const name of ["name", "title", "aria-label"] as const) {
    const value = element.getAttribute(name);
    if (value) add("stableAttribute", { strategy: "css", selector: `[${name}="${cssString(value)}"]` }, `[${name}="${cssString(value)}"]`);
  }
  const placeholder = element.getAttribute("placeholder");
  if (placeholder) addFiltered("placeholder", { strategy: "placeholder", text: placeholder },
    [...document.querySelectorAll("input,textarea")].filter((candidate) => candidate.getAttribute("placeholder") === placeholder));
  if (text && text.length <= 200) addFiltered("text", { strategy: "text", text, exact: true },
    [...document.querySelectorAll(element.tagName)].filter((candidate) => sanitizeText(candidate.textContent ?? undefined, 300) === text));
  if (element.id) add("stableId", { strategy: "css", selector: `#${cssEscape(element.id)}` }, `#${cssEscape(element.id)}`);
  const shortCss = shortCssFor(element);
  add("shortCss", { strategy: "css", selector: shortCss }, shortCss);
  const structuralCss = structuralCssFor(element);
  add("structuralCss", { strategy: "css", selector: structuralCss }, structuralCss);
  const xpath = xpathFor(element);
  addFiltered("xpath", { strategy: "xpath", selector: xpath }, evaluateXPath(xpath));
  return result;

  function add(key: CandidateObservation["key"], expression: Expression, selector: string): void {
    let matches: Element[] = [];
    try { matches = [...document.querySelectorAll(selector)]; } catch { /* diagnóstico abaixo */ }
    addFiltered(key, expression, matches);
  }
  function addFiltered(
    key: CandidateObservation["key"],
    expression: Expression,
    matches: Element[]
  ): void {
    const serialized = JSON.stringify(expression);
    result.push({
      key,
      expression,
      matchCount: matches.length,
      matchesTarget: matches.length === 1 && matches[0] === element,
      sensitive: /(?:password|passwd|secret|token|authorization|cookie|api[-_]?key)\s*[:=]/iu.test(serialized),
      dynamic: [...Object.values(expression)].some((value) => typeof value === "string" && isDynamicToken(value))
    });
  }
}

function prepareScreenshot(target: Element | undefined, overlays: HTMLElement[]): EvidenceMask[] {
  clearOverlays(overlays);
  const masks: EvidenceMask[] = [];
  for (const element of document.querySelectorAll("input,textarea,[data-rpa-sensitive]")) {
    if (!(element instanceof HTMLElement) || !isSensitiveField(element)) continue;
    const rect = element.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) continue;
    masks.push({ ...toRect(rect), reason: "sensitive-field" });
    overlays.push(createOverlay(rect, "mask"));
  }
  if (target !== undefined) overlays.push(createOverlay(target.getBoundingClientRect(), "highlight"));
  return masks;
}

function createOverlay(rect: DOMRect, kind: "mask" | "highlight"): HTMLElement {
  const node = document.createElement("div");
  node.dataset.rpaBlocklyOverlay = kind;
  node.style.cssText = [
    "all:initial", "position:fixed", `left:${rect.left}px`, `top:${rect.top}px`,
    `width:${rect.width}px`, `height:${rect.height}px`, "pointer-events:none",
    "z-index:2147483647",
    kind === "mask" ? "background:#111827" : "outline:3px solid #22c55e"
  ].join(";");
  document.documentElement.append(node);
  return node;
}

function clearOverlays(overlays: HTMLElement[]): void {
  while (overlays.length > 0) overlays.pop()?.remove();
}

function collectAttributes(element: Element): Record<string, string> {
  const allowed = new Set(["id", "name", "type", "role", "title", "placeholder", "aria-label", "data-testid", "data-test-id"]);
  return sanitizeAttributes(Object.fromEntries([...element.attributes]
    .filter((attribute) => allowed.has(attribute.name))
    .map((attribute) => [attribute.name, attribute.value])));
}

function relatedNodes(start: Element | null, direction: "parent"): FingerprintNodeSnapshot[] {
  const result: FingerprintNodeSnapshot[] = [];
  let current = start;
  while (current !== null && result.length < 32) {
    result.push(nodeSnapshot(current));
    current = direction === "parent" ? current.parentElement : null;
  }
  return result;
}

function siblingNodes(element: Element, direction: "previous" | "next"): FingerprintNodeSnapshot[] {
  const result: FingerprintNodeSnapshot[] = [];
  let current = direction === "previous" ? element.previousElementSibling : element.nextElementSibling;
  while (current !== null && result.length < 10) {
    result.push(nodeSnapshot(current));
    current = direction === "previous" ? current.previousElementSibling : current.nextElementSibling;
  }
  return result;
}

function nodeSnapshot(element: Element): FingerprintNodeSnapshot {
  const role = roleFor(element);
  const text = sanitizeText(element.textContent ?? undefined, 300);
  return {
    tagName: element.tagName.toLowerCase(),
    ...(role === undefined ? {} : { role }),
    ...(text === undefined ? {} : { text }),
    attributes: collectAttributes(element)
  };
}

function frameExpressions(): { expressions: Expression[]; inaccessible: boolean } {
  const result: Expression[] = [];
  let current: Window = window;
  try {
    while (current !== current.parent) {
      const frame = current.frameElement;
      if (frame === null) return { expressions: result, inaccessible: true };
      const parentDocument = current.parent.document;
      const expression = uniqueFrameExpression(frame, parentDocument);
      if (expression === undefined) return { expressions: result, inaccessible: true };
      result.unshift(expression);
      current = current.parent;
    }
  } catch {
    return { expressions: result, inaccessible: true };
  }
  return { expressions: result, inaccessible: false };
}

function uniqueFrameExpression(frame: Element, owner: Document): Expression | undefined {
  const candidates: Array<{ selector: string; expression: Expression }> = [];
  if (frame.id && !isDynamicToken(frame.id)) {
    candidates.push({ selector: `#${cssEscape(frame.id)}`, expression: { strategy: "css", selector: `#${cssEscape(frame.id)}` } });
  }
  for (const name of ["data-testid", "name", "title"] as const) {
    const value = frame.getAttribute(name);
    if (value && !isDynamicToken(value)) {
      const selector = `${frame.tagName.toLowerCase()}[${name}="${cssString(value)}"]`;
      candidates.push({ selector, expression: { strategy: "css", selector } });
    }
  }
  for (const candidate of candidates) {
    try {
      const matches = owner.querySelectorAll(candidate.selector);
      if (matches.length === 1 && matches[0] === frame) return candidate.expression;
    } catch { /* tenta o próximo candidato */ }
  }
  return undefined;
}

function targetElement(event: Event): Element | undefined {
  const candidate = event.composedPath()[0];
  return candidate instanceof Element ? candidate : event.target instanceof Element ? event.target : undefined;
}

function elementKey(element: Element): string {
  return stableId("target", location.origin, xpathFor(element));
}

function accessibleNameFor(element: Element): string | undefined {
  return sanitizeText(element.getAttribute("aria-label") ?? labelFor(element) ?? element.textContent ?? undefined, 300);
}

function labelFor(element: Element): string | undefined {
  if (element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement) {
    const labels = [...(element.labels ?? [])].map((label) => sanitizeText(label.textContent ?? undefined, 300))
      .filter((value): value is string => value !== undefined);
    return labels[0];
  }
  return undefined;
}

function elementsByLabel(text: string): Element[] {
  return [...document.querySelectorAll("input,select,textarea,button")].filter((candidate) => labelFor(candidate) === text);
}

function roleFor(element: Element): string | undefined {
  const explicit = element.getAttribute("role");
  if (explicit) return explicit;
  const tag = element.tagName.toLowerCase();
  if (tag === "button") return "button";
  if (tag === "a" && element.hasAttribute("href")) return "link";
  if (tag === "select") return "combobox";
  if (tag === "textarea") return "textbox";
  if (element instanceof HTMLInputElement) {
    if (element.type === "checkbox") return "checkbox";
    if (element.type === "radio") return "radio";
    if (["button", "submit", "reset"].includes(element.type)) return "button";
    return "textbox";
  }
  return undefined;
}

function isSensitiveField(element: Element): boolean {
  return element instanceof HTMLInputElement &&
    (element.type === "password" || /(?:current-password|new-password|one-time-code|cc-number|cc-csc)/iu.test(element.autocomplete)) ||
    element.hasAttribute("data-rpa-sensitive");
}

function formFor(element: Element | undefined): HTMLFormElement | undefined {
  if (element instanceof HTMLButtonElement || element instanceof HTMLInputElement ||
      element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement) {
    return element.form ?? undefined;
  }
  return element instanceof HTMLFormElement ? element : undefined;
}

function shortCssFor(element: Element): string {
  if (element.id && !isDynamicToken(element.id)) return `#${cssEscape(element.id)}`;
  const testId = element.getAttribute("data-testid");
  if (testId && !isDynamicToken(testId)) return `[data-testid="${cssString(testId)}"]`;
  const name = element.getAttribute("name");
  return name && !isDynamicToken(name)
    ? `${element.tagName.toLowerCase()}[name="${cssString(name)}"]`
    : element.tagName.toLowerCase();
}

function structuralCssFor(element: Element): string {
  const parts: string[] = [];
  let current: Element | null = element;
  while (current !== null && parts.length < 6) {
    let part = current.tagName.toLowerCase();
    const siblings = current.parentElement === null
      ? []
      : [...current.parentElement.children].filter((sibling) => sibling.tagName === current!.tagName);
    if (siblings.length > 1) part += `:nth-of-type(${siblings.indexOf(current) + 1})`;
    parts.unshift(part);
    current = current.parentElement;
  }
  return parts.join(" > ");
}

function xpathFor(element: Element): string {
  const parts: string[] = [];
  let current: Element | null = element;
  while (current !== null) {
    const tag = current.tagName.toLowerCase();
    const siblings = current.parentElement === null
      ? []
      : [...current.parentElement.children].filter((sibling) => sibling.tagName === current!.tagName);
    parts.unshift(`${tag}[${Math.max(1, siblings.indexOf(current) + 1)}]`);
    current = current.parentElement;
  }
  return `/${parts.join("/")}`;
}

function evaluateXPath(xpath: string): Element[] {
  const result: Element[] = [];
  const iterator = document.evaluate(xpath, document, null, XPathResult.ORDERED_NODE_ITERATOR_TYPE);
  let node = iterator.iterateNext();
  while (node !== null) {
    if (node instanceof Element) result.push(node);
    node = iterator.iterateNext();
  }
  return result;
}

function cssEscape(value: string): string {
  return CSS.escape(value);
}

function cssString(value: string): string {
  return value.replace(/\\/gu, "\\\\").replace(/"/gu, "\\\"");
}

function toRect(rect: DOMRect): { x: number; y: number; width: number; height: number } {
  return { x: rect.left, y: rect.top, width: rect.width, height: rect.height };
}
