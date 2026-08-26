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
  let hoverTimer: ReturnType<typeof setTimeout> | undefined;
  const targets = new Map<string, Element>();
  const pressedModifiers = new Map<string, { usedInChord: boolean }>();
  const reportedUnsupportedKinds = new Set<string>();
  const overlayNodes: HTMLElement[] = [];
  const startedAt = performance.timeOrigin + performance.now();

  const capture = async (
    domEvent: Event,
    requestedType: RawCaptureEvent["type"],
    requestedUnsupportedReason?: string
  ): Promise<void> => {
    if (!domEvent.isTrusted) return;
    let type = requestedType;
    let unsupportedReason = requestedUnsupportedReason;
    let element = targetElement(domEvent);
    if (type === "click" && element instanceof HTMLCanvasElement) {
      type = "unsupported";
      unsupportedReason =
        "Clique em canvas detectado. O catálogo V2 não possui clique por coordenadas; candidato: novo bloco clickAt.";
    } else if (type === "click") {
      const downloadTrigger = element?.closest("a[download],area[download]");
      if (downloadTrigger !== null && downloadTrigger !== undefined) {
        element = downloadTrigger;
        type = "download";
      }
    }
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
    if (unsupportedReason !== undefined) {
      base.unsupportedCode = "UNSUPPORTED_INTERACTION";
      base.unsupportedReason = unsupportedReason;
    }

    let transientSecret: string | undefined;
    if (type === "input" || type === "change") {
      if (element !== undefined && isSensitiveField(element)) {
        const value = editableValue(element);
        if (value === undefined) {
          base.type = "unsupported";
          base.unsupportedCode = "UNSUPPORTED_INTERACTION";
          base.unsupportedReason =
            "Um elemento marcado como sensível foi editado, mas seu valor não pôde ser lido com segurança.";
        } else if (options.captureSecrets) {
          transientSecret = value;
        }
      } else if (element instanceof HTMLInputElement) {
        base.value = element.type === "checkbox" || element.type === "radio"
          ? element.checked
          : element.value;
      } else if (element instanceof HTMLTextAreaElement) {
        base.value = element.value;
      } else if (element instanceof HTMLElement && element.isContentEditable) {
        base.value = element.textContent ?? "";
      } else {
        base.type = "unsupported";
        base.unsupportedCode = "UNSUPPORTED_INTERACTION";
        base.unsupportedReason =
          "Uma edição foi detectada, mas o elemento não é input, textarea nem contenteditable. É necessária revisão do widget.";
      }
    } else if (type === "select" && element instanceof HTMLSelectElement) {
      if (element.multiple) {
        base.type = "unsupported";
        base.unsupportedCode = "UNSUPPORTED_INTERACTION";
        base.unsupportedReason =
          "Seleção múltipla detectada. O bloco selectOption V2 atual aceita um único valor; decisão necessária para ampliar o contrato.";
      } else {
        base.value = element.value;
      }
    } else if (type === "keydown" && domEvent instanceof KeyboardEvent) {
      const key = playwrightKey(domEvent);
      if (key === undefined) {
        base.type = "unsupported";
        base.unsupportedCode = "UNSUPPORTED_INTERACTION";
        base.unsupportedReason =
          "Uma tecla modificadora isolada foi usada. O catálogo V2 não possui keyDown/keyUp; decisão necessária para um novo bloco.";
      } else {
        base.key = key;
      }
    } else if (type === "upload" && element instanceof HTMLInputElement) {
      if (element.files === null || element.files.length === 0) {
        base.type = "unsupported";
        base.unsupportedCode = "UNSUPPORTED_INTERACTION";
        base.unsupportedReason =
          "A seleção de arquivo foi limpa. O bloco upload V2 atual não representa uma lista vazia.";
      } else if (element.files.length > 1) {
        base.type = "unsupported";
        base.unsupportedCode = "UNSUPPORTED_INTERACTION";
        base.unsupportedReason =
          "Upload de múltiplos arquivos detectado. O bloco upload V2 atual aceita um arquivo; decisão necessária para ampliar o contrato.";
      } else {
        try {
          base.upload = await captureUpload(element.files[0]!, options.includeUploads);
        } catch (error) {
          base.type = "unsupported";
          base.unsupportedCode = "UNSUPPORTED_INTERACTION";
          base.unsupportedReason = error instanceof Error
            ? error.message
            : "O upload não atende à política de segurança.";
        }
      }
    }
    const request: RecorderRequest = {
      type: "RECORDER_CAPTURE_EVENT",
      event: base,
      ...(transientSecret === undefined ? {} : { transientSecret })
    };
    await chrome.runtime.sendMessage(request);
    transientSecret = undefined;
  };

  const captureUnsupportedOnce = (kind: string, event: Event, reason: string): void => {
    if (reportedUnsupportedKinds.has(kind)) return;
    reportedUnsupportedKinds.add(kind);
    void capture(event, "unsupported", reason);
  };

  document.addEventListener("click", (event) => void capture(event, "click"), true);
  document.addEventListener("input", (event) => {
    const element = targetElement(event);
    if (element instanceof HTMLSelectElement ||
        element instanceof HTMLInputElement && element.type === "file") return;
    void capture(event, "input");
  }, true);
  document.addEventListener("change", (event) => {
    const element = targetElement(event);
    void capture(event, element instanceof HTMLSelectElement
      ? "select"
      : element instanceof HTMLInputElement && element.type === "file" ? "upload" : "change");
  }, true);
  document.addEventListener("submit", (event) => void capture(event, "submit"), true);
  document.addEventListener("keydown", (event) => {
    if (isModifierKey(event.key)) {
      pressedModifiers.set(event.key, { usedInChord: false });
      return;
    }
    for (const state of pressedModifiers.values()) state.usedInChord = true;
    void capture(event, "keydown");
  }, true);
  document.addEventListener("keyup", (event) => {
    const modifier = pressedModifiers.get(event.key);
    if (modifier === undefined) return;
    pressedModifiers.delete(event.key);
    if (!modifier.usedInChord) {
      captureUnsupportedOnce(
        "isolated-modifier",
        event,
        "Uma tecla modificadora isolada foi usada. O catálogo V2 não possui keyDown/keyUp; decisão necessária para um novo bloco."
      );
    }
  }, true);
  document.addEventListener("contextmenu", (event) => captureUnsupportedOnce(
    "contextmenu",
    event,
    "Clique com o botão direito detectado. O catálogo V2 não distingue botões do mouse; candidato: ampliar click ou criar rightClick."
  ), true);
  document.addEventListener("dblclick", (event) => captureUnsupportedOnce(
    "dblclick",
    event,
    "Clique duplo detectado. Dois cliques simples não preservam necessariamente a mesma semântica; candidato: ampliar click ou criar doubleClick."
  ), true);
  document.addEventListener("dragstart", (event) => captureUnsupportedOnce(
    "drag-and-drop",
    event,
    "Arraste detectado. O catálogo V2 não possui origem e destino de drag-and-drop; candidato: novo bloco dragAndDrop."
  ), true);
  document.addEventListener("drop", (event) => captureUnsupportedOnce(
    "drop",
    event,
    "Soltura de item detectada. O catálogo V2 não possui origem e destino de drag-and-drop; candidato: novo bloco dragAndDrop."
  ), true);
  document.addEventListener("copy", (event) => captureUnsupportedOnce(
    "copy",
    event,
    "Cópia para a área de transferência detectada. O catálogo V2 não possui bloco de clipboard; decisão necessária antes de capturar esse conteúdo."
  ), true);
  document.addEventListener("cut", (event) => captureUnsupportedOnce(
    "cut",
    event,
    "Recorte para a área de transferência detectado. O catálogo V2 não possui bloco de clipboard; decisão necessária antes de capturar esse conteúdo."
  ), true);
  document.addEventListener("scroll", (event) => captureUnsupportedOnce(
    "scroll",
    event,
    "Rolagem manual detectada. O catálogo V2 não possui uma ação de scroll; candidato: novo bloco scroll com alvo e posição."
  ), true);
  document.addEventListener("pointerover", (event) => {
    if (!(event instanceof PointerEvent) || event.pointerType !== "mouse") return;
    const target = targetElement(event)?.closest(
      "a,button,[aria-haspopup],[role='button'],[role='menuitem'],[role='option'],[title]"
    );
    if (target === null || target === undefined) return;
    if (hoverTimer !== undefined) clearTimeout(hoverTimer);
    hoverTimer = setTimeout(() => captureUnsupportedOnce(
      "hover",
      event,
      "Permanência do ponteiro sobre um controle detectada. O catálogo V2 não possui hover; candidato: novo bloco hover."
    ), 700);
  }, true);
  document.addEventListener("pointerout", () => {
    if (hoverTimer !== undefined) clearTimeout(hoverTimer);
    hoverTimer = undefined;
  }, true);

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
  let candidates = candidateObservations(
    element,
    role,
    accessibleName,
    text,
    document);
  let scope: Expression | undefined;
  if (!candidates.some((candidate) =>
    candidate.matchCount === 1 && candidate.matchesTarget &&
    !candidate.sensitive && !candidate.dynamic &&
    candidate.key !== "structuralCss" && candidate.key !== "xpath")) {
    const stableScope = findStableScope(element);
    if (stableScope !== undefined) {
      scope = stableScope.expression;
      candidates = candidateObservations(
        element,
        role,
        accessibleName,
        text,
        stableScope.element);
    }
  }
  return {
    tagName: element.tagName.toLowerCase(),
    ...(role === undefined ? {} : { role }),
    ...(accessibleName === undefined ? {} : { accessibleName }),
    ...(text === undefined ? {} : { text }),
    attributes,
    ancestors: relatedNodes(element.parentElement, "parent"),
    previousSiblings: siblingNodes(element, "previous"),
    nextSiblings: siblingNodes(element, "next"),
    candidates,
    frames: frameContext.expressions,
    ...(scope === undefined ? {} : { scope }),
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
  text: string | undefined,
  root: Document | ShadowRoot | Element
): CandidateObservation[] {
  const result: CandidateObservation[] = [];
  const testId = element.getAttribute("data-testid") ?? element.getAttribute("data-test-id");
  if (testId) add("testId", { strategy: "testId", text: testId }, `[data-testid="${cssString(testId)}"],[data-test-id="${cssString(testId)}"]`);
  if (role && accessibleName) addFiltered("role", { strategy: "role", role, name: accessibleName },
    allElements(root).filter((candidate) => roleFor(candidate) === role && accessibleNameFor(candidate) === accessibleName));
  const label = labelFor(element);
  if (label) addFiltered("label", { strategy: "label", text: label }, elementsByLabel(label, root));
  for (const name of ["name", "title", "aria-label"] as const) {
    const value = element.getAttribute(name);
    if (value) add("stableAttribute", { strategy: "css", selector: `[${name}="${cssString(value)}"]` }, `[${name}="${cssString(value)}"]`);
  }
  const placeholder = element.getAttribute("placeholder");
  if (placeholder) addFiltered("placeholder", { strategy: "placeholder", text: placeholder },
    querySelectorAllDeep("input,textarea", root).filter((candidate) => candidate.getAttribute("placeholder") === placeholder));
  if (text && text.length <= 200) addFiltered("text", { strategy: "text", text, exact: true },
    querySelectorAllDeep(element.tagName, root).filter((candidate) => sanitizeText(candidate.textContent ?? undefined, 300) === text));
  if (element.id) add("stableId", { strategy: "css", selector: `#${cssEscape(element.id)}` }, `#${cssEscape(element.id)}`);
  const shortCss = shortCssFor(element);
  add("shortCss", { strategy: "css", selector: shortCss }, shortCss);
  const structuralCss = structuralCssFor(
    element,
    root instanceof Element ? root : undefined);
  add("structuralCss", { strategy: "css", selector: structuralCss }, structuralCss);
  const xpath = xpathFor(element, root instanceof Element ? root : undefined);
  addFiltered(
    "xpath",
    { strategy: "xpath", selector: xpath },
    root instanceof ShadowRoot ? [] : evaluateXPath(xpath, root));
  return result;

  function add(key: CandidateObservation["key"], expression: Expression, selector: string): void {
    let matches: Element[] = [];
    try { matches = querySelectorAllDeep(selector, root); } catch { /* diagnóstico abaixo */ }
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

function findStableScope(
  element: Element
): { element: Element; expression: Expression } | undefined {
  let current = composedParent(element);
  while (current !== undefined) {
    const candidates: Array<{ selector: string; expression: Expression }> = [];
    if (current.id && !isDynamicToken(current.id)) {
      const selector = `#${cssEscape(current.id)}`;
      candidates.push({ selector, expression: { strategy: "css", selector } });
    }
    for (const name of ["data-testid", "data-test-id", "name"] as const) {
      const value = current.getAttribute(name);
      if (value && !isDynamicToken(value) && !isSensitiveToken(name, value)) {
        const selector = `[${name}="${cssString(value)}"]`;
        candidates.push({ selector, expression: { strategy: "css", selector } });
      }
    }
    for (const candidate of candidates) {
      const matches = querySelectorAllDeep(candidate.selector, document);
      if (matches.length === 1 && matches[0] === current) {
        return { element: current, expression: candidate.expression };
      }
    }
    current = composedParent(current);
  }
  return undefined;
}

function composedParent(element: Element): Element | undefined {
  if (element.parentElement !== null) return element.parentElement;
  const root = element.getRootNode();
  return root instanceof ShadowRoot ? root.host : undefined;
}

function querySelectorAllDeep(
  selector: string,
  root: Document | ShadowRoot | Element
): Element[] {
  const result = new Set<Element>();
  visit(root);
  return [...result];

  function visit(current: Document | ShadowRoot | Element): void {
    for (const match of current.querySelectorAll(selector)) result.add(match);
    for (const candidate of current.querySelectorAll("*")) {
      if (candidate.shadowRoot !== null) visit(candidate.shadowRoot);
    }
  }
}

function allElements(root: Document | ShadowRoot | Element): Element[] {
  return querySelectorAllDeep("*", root);
}

function isSensitiveToken(name: string, value: string): boolean {
  return /(?:password|passwd|secret|token|authorization|cookie|api[-_]?key)/iu.test(
    `${name}=${value}`);
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

function elementsByLabel(
  text: string,
  root: Document | ShadowRoot | Element
): Element[] {
  return querySelectorAllDeep("input,select,textarea,button", root)
    .filter((candidate) => labelFor(candidate) === text);
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

function editableValue(element: Element): string | undefined {
  if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) return element.value;
  if (element instanceof HTMLElement && element.isContentEditable) return element.textContent ?? "";
  return undefined;
}

function playwrightKey(event: KeyboardEvent): string | undefined {
  if (event.isComposing || ["Dead", "Process", "Unidentified"].includes(event.key)) return undefined;
  const modifierKeys = new Set(["Alt", "Control", "Meta", "Shift", "AltGraph"]);
  if (modifierKeys.has(event.key)) return undefined;
  const key = event.key === " " ? "Space" : event.key;
  if (event.getModifierState("AltGraph")) return key;
  const printable = [...key].length === 1;
  const modifiers = [
    event.ctrlKey ? "Control" : undefined,
    event.altKey ? "Alt" : undefined,
    event.metaKey ? "Meta" : undefined,
    event.shiftKey && (!printable || event.ctrlKey || event.altKey || event.metaKey) ? "Shift" : undefined
  ].filter((value): value is string => value !== undefined);
  return modifiers.length === 0 ? key : `${modifiers.join("+")}+${key}`;
}

function isModifierKey(key: string): boolean {
  return ["Alt", "Control", "Meta", "Shift", "AltGraph"].includes(key);
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

function structuralCssFor(element: Element, stopAt?: Element): string {
  const parts: string[] = [];
  let current: Element | null = element;
  while (current !== null && current !== stopAt && parts.length < 6) {
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

function xpathFor(element: Element, stopAt?: Element): string {
  const parts: string[] = [];
  let current: Element | null = element;
  while (current !== null && current !== stopAt) {
    const tag = current.tagName.toLowerCase();
    const siblings = current.parentElement === null
      ? []
      : [...current.parentElement.children].filter((sibling) => sibling.tagName === current!.tagName);
    parts.unshift(`${tag}[${Math.max(1, siblings.indexOf(current) + 1)}]`);
    current = current.parentElement;
  }
  return stopAt === undefined ? `/${parts.join("/")}` : `.//${parts.join("/")}`;
}

function evaluateXPath(
  xpath: string,
  root: Document | Element
): Element[] {
  const result: Element[] = [];
  const owner = root instanceof Document ? root : root.ownerDocument;
  const iterator = owner.evaluate(xpath, root, null, XPathResult.ORDERED_NODE_ITERATOR_TYPE);
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
