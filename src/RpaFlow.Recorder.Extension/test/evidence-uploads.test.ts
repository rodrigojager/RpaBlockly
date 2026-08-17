import assert from "node:assert/strict";
import test from "node:test";
import { ScreenshotRateLimiter, slideshowItems } from "../src/evidence/evidence.js";
import {
  captureUpload,
  maximumTotalUploadBytes,
  maximumUploadBytes,
  validateUploadTotals
} from "../src/uploads/uploads.js";

test("rate limit e slideshow são estáticos", () => {
  const limiter = new ScreenshotRateLimiter(600);
  assert.equal(limiter.tryAcquire(1_000), true);
  assert.equal(limiter.tryAcquire(1_200), false);
  assert.equal(limiter.tryAcquire(1_600), true);
  const items = slideshowItems([]);
  assert.deepEqual(items, []);
  assert.doesNotMatch(JSON.stringify(items), /navigate|execute|iframe/iu);
});

test("upload registra metadados e só inclui bytes por consentimento", async () => {
  const file = new File(["conteúdo"], "Relatório.csv", { type: "text/csv" });
  const metadataOnly = await captureUpload(file, false);
  const included = await captureUpload(file, true);
  assert.equal(metadataOnly.name, "relatorio.csv");
  assert.equal(metadataOnly.contentBase64, undefined);
  assert.equal(typeof included.contentBase64, "string");
  validateUploadTotals([included]);
});

test("upload bloqueia tipo perigoso, arquivo grande e soma acima da cota", async () => {
  await assert.rejects(
    captureUpload(new File(["perigoso"], "executar.exe"), true),
    /tipo de arquivo/u);
  await assert.rejects(
    captureUpload(
      new File([new Uint8Array(maximumUploadBytes + 1)], "grande.pdf"),
      true),
    /20 MiB/u);
  assert.throws(() => validateUploadTotals([
    {
      name: "parte-a.pdf",
      mimeType: "application/pdf",
      size: maximumTotalUploadBytes / 2 + 1,
      included: true
    },
    {
      name: "parte-b.pdf",
      mimeType: "application/pdf",
      size: maximumTotalUploadBytes / 2,
      included: true
    }
  ]), /50 MiB/u);
});
