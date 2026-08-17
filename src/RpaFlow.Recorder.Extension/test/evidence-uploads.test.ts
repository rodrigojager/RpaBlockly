import assert from "node:assert/strict";
import test from "node:test";
import { ScreenshotRateLimiter, slideshowItems } from "../src/evidence/evidence.js";
import { captureUpload, validateUploadTotals } from "../src/uploads/uploads.js";

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
