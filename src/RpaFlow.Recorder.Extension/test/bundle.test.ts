import assert from "node:assert/strict";
import test from "node:test";
import { unzipSync } from "fflate";
import { buildBundle, verifyBundleIntegrity } from "../src/bundle/bundle.js";
import { canonicalJson } from "../src/core/stable.js";
import { generatePackage } from "../src/package/generator.js";
import { checkpoint, rawEvent } from "./fixtures.js";

test("mesmo conteúdo lógico gera ZIP idêntico e sem replay", async () => {
  const events = [rawEvent(1, "navigation", { target: undefined, targetKey: undefined })];
  const state = checkpoint(events);
  const generated = generatePackage(state.name, events);
  const input = {
    bundleId: "bundle-fixture-001",
    createdAtUtc: "2026-08-17T18:01:00.000Z",
    checkpoint: state,
    generated,
    evidence: [],
    secrets: [],
    comments: []
  };
  const first = await buildBundle(input);
  const second = await buildBundle(input);
  assert.deepEqual(first.bytes, second.bytes);
  assert.equal(first.manifest.containsReplay, false);
  assert.equal([...first.entries.keys()].some((path) => /replay/iu.test(path)), false);
  await verifyBundleIntegrity(first.bytes);
});

test("adulteração de entrada é detectada pela integridade", async () => {
  const events = [rawEvent(1, "navigation", { target: undefined, targetKey: undefined })];
  const state = checkpoint(events);
  const built = await buildBundle({
    bundleId: "bundle-fixture-002",
    createdAtUtc: "2026-08-17T18:01:00.000Z",
    checkpoint: state,
    generated: generatePackage(state.name, events),
    evidence: [], secrets: [], comments: []
  });
  const entries = unzipSync(built.bytes);
  entries["package/flow.production.json"] = new TextEncoder().encode(canonicalJson({ adulterado: true }));
  const { zipSync } = await import("fflate");
  const tampered = zipSync(entries);
  await assert.rejects(() => verifyBundleIntegrity(tampered), /Integridade inválida/u);
});
