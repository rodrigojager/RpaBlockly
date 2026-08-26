import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { performance } from "node:perf_hooks";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";
import { assertNoDynamicCode } from "../scripts/csp.mjs";
import { buildBundle } from "../src/bundle/bundle.js";
import { generatePackage } from "../src/package/generator.js";
import { checkpoint, rawEvent } from "./fixtures.js";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");

test("side panel mantém contratos mínimos de acessibilidade", async () => {
  const html = await readFile(join(root, "src", "sidepanel", "index.html"), "utf8");
  const css = await readFile(join(root, "src", "sidepanel", "styles.css"), "utf8");
  assert.match(html, /<html lang="pt-BR">/u);
  assert.match(html, /role="status" aria-live="polite"/u);
  assert.match(html, /id="recording-indicator"[^>]+aria-label="Gravação em andamento"/u);
  assert.match(html, /id="evidence-capture-status"[^>]+role="status"/u);
  assert.match(html, /class="recording-dot"/u);
  assert.match(html, /id="page-target"[^>]+data-state="checking"[^>]+role="note"/u);
  assert.match(html, /id="timeline"[^>]+aria-live="polite"/u);
  assert.match(html, /class="step-thumbnail"/u);
  assert.match(html, /<progress[^>]+aria-label="Progresso da exportação"/u);
  for (const id of [
    "session-name", "secret-sharing-password", "recovery-key",
    "recipient-key-id", "recipient-public-key"
  ]) {
    assert.match(html, new RegExp(`<label for="${id}">`, "u"));
  }
  assert.match(html, /É o texto curto que você escolhe e repassa ao desenvolvedor/u);
  assert.match(html, /É um código longo gerado pelo Recorder\. Não é a sua senha/u);
  assert.doesNotMatch(html, /sem pedir conhecimentos técnicos/u);
  assert.match(css, /input:not\(\[type="checkbox"\]\):not\(\[type="radio"\]\)/u);
  assert.match(css, /grid-template-columns: 17px minmax\(0, 1fr\) auto/u);
  assert.match(css, /button:focus-visible/u);
  assert.match(css, /@keyframes recording-pulse/u);
  assert.match(css, /\.page-target\[data-state="blocked"\]/u);
  assert.match(css, /\.step-thumbnail/u);
  assert.match(css, /prefers-reduced-motion/u);
});

test("autorização ampla é opcional, explícita e persistente", async () => {
  const manifest = JSON.parse(await readFile(join(root, "manifest.json"), "utf8")) as {
    permissions: string[];
    optional_permissions?: string[];
    optional_host_permissions?: string[];
  };
  const sidepanel = await readFile(join(root, "src", "sidepanel", "sidepanel.ts"), "utf8");
  const serviceWorker = await readFile(join(root, "src", "background", "service-worker.ts"), "utf8");
  assert.deepEqual(manifest.optional_permissions, undefined);
  assert.deepEqual(manifest.optional_host_permissions, ["<all_urls>"]);
  assert.ok(!manifest.permissions.includes("tabs"));
  assert.ok(manifest.permissions.includes("activeTab"));
  assert.match(sidepanel, /chrome\.permissions\.request\(\{ origins: continuousHostOrigins \}\)/u);
  assert.doesNotMatch(serviceWorker, /chrome\.permissions\.request/u);
  assert.match(serviceWorker, /chrome\.permissions\.contains\(\{ origins: continuousHostOrigins \}\)/u);
  assert.match(serviceWorker, /chrome\.permissions\.onRemoved\.addListener/u);
  assert.match(serviceWorker, /Restabeleça o acesso amplo antes de retomar/u);
  assert.match(serviceWorker, /reconnectRecordingToInvokedTab\(tab\)/u);
  assert.match(serviceWorker, /chrome\.scripting\.executeScript/u);
});

test("painel acompanha a gravação ao vivo e mantém a aba escolhida", async () => {
  const sidepanel = await readFile(join(root, "src", "sidepanel", "sidepanel.ts"), "utf8");
  const serviceWorker = await readFile(join(root, "src", "background", "service-worker.ts"), "utf8");
  const messages = await readFile(join(root, "src", "shared", "messages.ts"), "utf8");
  const bundle = await readFile(join(root, "src", "bundle", "bundle.ts"), "utf8");
  assert.match(sidepanel, /chrome\.storage\.onChanged\.addListener/u);
  assert.match(sidepanel, /scheduleCheckpointRender/u);
  assert.match(sidepanel, /friendlyIntentTitle/u);
  assert.doesNotMatch(messages, /temporaryOrigins/u);
  assert.match(serviceWorker, /chrome\.tabs\.get\(request\.tabId\)/u);
  assert.match(messages, /"RECORDER_GET_TARGET"/u);
  assert.match(serviceWorker, /rememberRecorderTarget\(tab\)/u);
  assert.match(serviceWorker, /chrome\.sidePanel\.open\(\{ tabId: tab\.id \}\)/u);
  assert.match(serviceWorker, /openPanelOnActionClick: false/u);
  assert.match(sidepanel, /lastFocusedWindow: true/u);
  assert.match(messages, /interface RecorderAccessNotice/u);
  assert.match(serviceWorker, /saveRecorderAccessNotice/u);
  assert.match(sidepanel, /Acesso amplo precisa ser concedido novamente/u);
  assert.doesNotMatch(serviceWorker, /unsupportedReason: originReconnect/u);
  assert.match(serviceWorker, /cleanupLegacyTemporaryPermissions/u);
  assert.match(bundle, /origins: sessionOrigins\(input\.checkpoint\)/u);
  assert.match(serviceWorker, /RPABLOCKLY_RECORDER_REFRESH/u);
});

test("bundles MV3 não contêm eval nem Function dinâmica", async () => {
  const output = await mkdtemp(join(tmpdir(), "rpablockly-recorder-csp-"));
  try {
    await build({
      absWorkingDir: root,
      bundle: true,
      entryPoints: {
        "background/service-worker": "src/background/service-worker.ts",
        "sidepanel/sidepanel": "src/sidepanel/sidepanel.ts"
      },
      format: "esm",
      legalComments: "none",
      minify: false,
      outdir: output,
      platform: "browser",
      sourcemap: false,
      target: "chrome116",
      treeShaking: true
    });
    await assertNoDynamicCode(output);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
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
