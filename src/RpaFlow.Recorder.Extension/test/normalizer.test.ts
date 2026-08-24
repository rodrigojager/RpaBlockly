import assert from "node:assert/strict";
import test from "node:test";
import { canonicalJson } from "../src/core/stable.js";
import { normalizeEvents } from "../src/capture/normalizer.js";
import { rawEvent } from "./fixtures.js";

test("digitação contínua e change geram um único fill determinístico", () => {
  const events = [
    rawEvent(1, "input", { value: "R" }),
    rawEvent(2, "input", { value: "Ro" }),
    rawEvent(3, "change", { value: "Rodrigo" })
  ];
  const first = normalizeEvents(events);
  const second = normalizeEvents([...events].reverse());
  assert.equal(first.intents.length, 1);
  assert.equal(first.intents[0]?.type, "fill");
  assert.equal(first.intents[0]?.value, "Rodrigo");
  assert.equal(canonicalJson(first), canonicalJson(second));
});

test("input secreto sem opt-in vira pendência e nunca ação inventada", () => {
  const result = normalizeEvents([rawEvent(1, "input", { value: undefined })]);
  assert.equal(result.intents.length, 0);
  assert.equal(result.issues[0]?.code, "SECRET_NOT_CAPTURED");
  assert.equal(result.issues[0]?.severity, "blocking");
});

test("interação desconhecida é omitida com issue estável", () => {
  const result = normalizeEvents([rawEvent(1, "unsupported", {
    target: undefined,
    unsupportedReason: "Arraste complexo"
  })]);
  assert.equal(result.intents.length, 0);
  assert.equal(result.issues[0]?.code, "UNSUPPORTED_INTERACTION");
});

test("iframe inacessível e shadow root fechado geram códigos específicos", () => {
  const frame = normalizeEvents([rawEvent(1, "click", {
    target: { ...rawEvent(1, "click").target!, inaccessibleFrame: true }
  })]);
  const shadow = normalizeEvents([rawEvent(1, "click", {
    target: { ...rawEvent(1, "click").target!, closedShadowRoot: true }
  })]);
  assert.equal(frame.issues[0]?.code, "CROSS_ORIGIN_FRAME_NOT_CAPTURED");
  assert.equal(shadow.issues[0]?.code, "UNSUPPORTED_CLOSED_SHADOW_ROOT");
});

test("clique simples, clique duplo, checkbox, radio e select preservam a intenção", () => {
  const result = normalizeEvents([
    rawEvent(1, "click", { targetKey: "simple" }),
    rawEvent(2, "click", { targetKey: "double" }),
    rawEvent(3, "click", { targetKey: "double" }),
    rawEvent(4, "change", { targetKey: "checkbox", value: true }),
    rawEvent(5, "change", { targetKey: "radio", value: true }),
    rawEvent(6, "select", { targetKey: "state", value: "SP" })
  ]);
  assert.deepEqual(
    result.intents.map((intent) => intent.type),
    ["click", "click", "click", "setChecked", "setChecked", "selectOption"]);
});

test("paste/input/change mantém apenas o valor final", () => {
  const result = normalizeEvents([
    rawEvent(1, "input", { value: "texto colado" }),
    rawEvent(2, "change", { value: "texto colado final" })
  ]);
  assert.equal(result.intents.length, 1);
  assert.equal(result.intents[0]?.type, "fill");
  assert.equal(result.intents[0]?.value, "texto colado final");
});

test("submit causal por clique ou Enter não duplica a ação", () => {
  const click = rawEvent(1, "click", { formKey: "form-a" });
  const submitAfterClick = rawEvent(2, "submit", {
    targetKey: "form-a",
    formKey: "form-a"
  });
  const enter = rawEvent(3, "keydown", { formKey: "form-b", key: "Enter" });
  const submitAfterEnter = rawEvent(4, "submit", {
    targetKey: "form-b",
    formKey: "form-b"
  });
  const result = normalizeEvents([click, submitAfterClick, enter, submitAfterEnter]);
  assert.deepEqual(result.intents.map((intent) => intent.type), ["click", "pressKey"]);
});

test("navegação inicial, SPA causal e popup são normalizados sem replay inventado", () => {
  const navigation = rawEvent(1, "navigation", {
    target: undefined,
    targetKey: undefined,
    url: "https://fixture.test/form"
  });
  const click = rawEvent(2, "click", { targetKey: "popup-trigger" });
  const spa = rawEvent(3, "navigation", {
    target: undefined,
    targetKey: undefined,
    causalEventId: click.id,
    navigationKind: "spa"
  });
  const popup = rawEvent(4, "popup", {
    target: undefined,
    targetKey: undefined,
    tabId: "tab-2",
    causalEventId: click.id,
    navigationKind: "popup"
  });
  const ready = rawEvent(5, "click", { tabId: "tab-2", targetKey: "popup-ready" });
  const result = normalizeEvents([navigation, click, spa, popup, ready]);
  assert.deepEqual(
    result.intents.map((intent) => intent.type),
    ["navigate", "clickAndSwitchPage", "click"]);
  assert.equal(result.intents[1]?.readyLocatorId !== undefined, true);
});

test("shadow root aberto permanece executável", () => {
  const target = rawEvent(1, "click").target!;
  const result = normalizeEvents([rawEvent(1, "click", {
    target: { ...target, closedShadowRoot: false }
  })]);
  assert.equal(result.intents[0]?.type, "click");
  assert.equal(result.issues.length, 0);
});
