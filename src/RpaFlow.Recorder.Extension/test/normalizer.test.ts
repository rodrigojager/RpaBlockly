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
