import assert from "node:assert/strict";
import test from "node:test";
import { unzipSync } from "fflate";
import { buildBundle, verifyBundleIntegrity } from "../src/bundle/bundle.js";
import { canonicalJson } from "../src/core/stable.js";
import type { EvidenceAsset } from "../src/evidence/evidence.js";
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

test("sessão registra todas as origens HTTP(S) navegadas", async () => {
  const events = [
    rawEvent(1, "navigation", {
      target: undefined,
      targetKey: undefined,
      url: "https://inicio.example/form"
    }),
    rawEvent(2, "navigation", {
      target: undefined,
      targetKey: undefined,
      url: "https://destino.example/resultado"
    })
  ];
  const state = {
    ...checkpoint(events),
    origin: "https://inicio.example"
  };
  const built = await buildBundle({
    bundleId: "bundle-multiorigin-fixture",
    createdAtUtc: "2026-08-17T18:01:00.000Z",
    checkpoint: state,
    generated: generatePackage(state.name, events),
    evidence: [], secrets: [], comments: []
  });
  const entries = unzipSync(built.bytes);
  const sessionBytes = entries["recording/session.json"];
  assert.ok(sessionBytes !== undefined);
  const session = JSON.parse(new TextDecoder().decode(sessionBytes)) as { origins: string[] };
  assert.deepEqual(session.origins, ["https://inicio.example", "https://destino.example"]);
});

test("evidência salva entra no ZIP e fica associada à ação", async () => {
  const event = rawEvent(1, "navigation", { target: undefined, targetKey: undefined });
  const state = {
    ...checkpoint([event]),
    options: { captureScreenshots: true, captureSecrets: false, includeUploads: false }
  };
  const generated = generatePackage(state.name, state.events);
  const intent = generated.intents[0];
  assert.ok(intent !== undefined);
  const image = new Uint8Array([82, 73, 70, 70, 1, 2, 3, 4]);
  const thumbnail = new Uint8Array([82, 73, 70, 70, 5, 6]);
  const evidence: EvidenceAsset = {
    metadata: {
      id: "evidence-fixture-001",
      eventId: event.id,
      actionId: intent.actionId,
      kind: "after",
      path: "evidence/evidence-fixture-001.webp",
      thumbnailPath: "evidence/thumbnails/evidence-fixture-001.webp",
      mimeType: "image/webp",
      width: 1280,
      height: 720,
      byteLength: image.length,
      capturedAtUtc: event.capturedAtUtc,
      masks: []
    },
    image,
    thumbnail
  };
  const built = await buildBundle({
    bundleId: "bundle-evidence-fixture",
    createdAtUtc: "2026-08-17T18:01:00.000Z",
    checkpoint: state,
    generated,
    evidence: [evidence],
    secrets: [],
    comments: []
  });
  const entries = unzipSync(built.bytes);
  assert.deepEqual(entries[evidence.metadata.path], image);
  assert.deepEqual(entries[evidence.metadata.thumbnailPath], thumbnail);
  const index = JSON.parse(new TextDecoder().decode(entries["evidence/index.json"])) as {
    items: Array<{ id: string }>;
  };
  assert.equal(index.items[0]?.id, evidence.metadata.id);
  const session = JSON.parse(new TextDecoder().decode(entries["recording/session.json"])) as {
    associations: Array<{ evidenceId?: string }>;
  };
  assert.equal(session.associations[0]?.evidenceId, evidence.metadata.id);
  await verifyBundleIntegrity(built.bytes);
});
