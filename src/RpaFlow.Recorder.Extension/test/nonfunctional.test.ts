import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { performance } from "node:perf_hooks";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { buildBundle } from "../src/bundle/bundle.js";
import { generatePackage } from "../src/package/generator.js";
import { checkpoint, rawEvent } from "./fixtures.js";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");

test("side panel mantém contratos mínimos de acessibilidade", async () => {
  const html = await readFile(join(root, "src", "sidepanel", "index.html"), "utf8");
  const css = await readFile(join(root, "src", "sidepanel", "styles.css"), "utf8");
  assert.match(html, /<html lang="pt-BR">/u);
  assert.match(html, /role="status" aria-live="polite"/u);
  assert.match(html, /<progress[^>]+aria-label="Progresso da exportação"/u);
  for (const id of ["session-name", "recipient-key-id", "recipient-public-key"]) {
    assert.match(html, new RegExp(`<label for="${id}">`, "u"));
  }
  assert.match(css, /button:focus-visible/u);
  assert.match(css, /prefers-reduced-motion/u);
});

test("gravação extensa respeita orçamento de tempo e memória", async () => {
  const events = Array.from({ length: 750 }, (_, index) => rawEvent(index + 1, "click", {
    id: `event-load-${String(index + 1).padStart(4, "0")}`
  }));
  const state = checkpoint(events);
  const before = process.memoryUsage().heapUsed;
  const started = performance.now();
  const generated = generatePackage(state.name, events);
  const bundle = await buildBundle({
    bundleId: "bundle-performance-fixture",
    createdAtUtc: "2026-08-17T18:01:00.000Z",
    checkpoint: state,
    generated,
    evidence: [],
    secrets: [],
    comments: []
  });
  const elapsed = performance.now() - started;
  const allocated = Math.max(0, process.memoryUsage().heapUsed - before);
  assert.equal(generated.flow.actions.length, 750);
  assert.ok(bundle.bytes.length < 5 * 1024 * 1024);
  assert.ok(elapsed < 10_000, `processamento levou ${Math.round(elapsed)} ms`);
  assert.ok(allocated < 192 * 1024 * 1024, `alocação cresceu ${allocated} bytes`);
});
