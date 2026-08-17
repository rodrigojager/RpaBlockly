import assert from "node:assert/strict";
import test from "node:test";
import { RecorderCheckpointStore, assertNoPlaintextSecret } from "../src/core/checkpoint-store.js";
import { createCheckpoint, transition } from "../src/core/state-machine.js";
import type { SessionStorageAdapter } from "../src/core/types.js";
import { rawEvent } from "./fixtures.js";

class MemoryStorage implements SessionStorageAdapter {
  private readonly values = new Map<string, unknown>();
  public async get<T>(key: string): Promise<T | undefined> { return this.values.get(key) as T | undefined; }
  public async set<T>(key: string, value: T): Promise<void> { this.values.set(key, value); }
  public async remove(key: string): Promise<void> { this.values.delete(key); }
}

test("máquina de estados recupera checkpoint após suspensão", async () => {
  const clock = { now: () => new Date("2026-08-17T18:00:00.000Z") };
  let checkpoint = createCheckpoint("Teste", "https://fixture.test/form", {
    captureScreenshots: true,
    captureSecrets: false,
    includeUploads: false
  }, clock);
  checkpoint = transition(checkpoint, "recording", clock);
  checkpoint = { ...checkpoint, events: [rawEvent(1, "click")], nextSequence: 2 };
  const adapter = new MemoryStorage();
  await new RecorderCheckpointStore(adapter).save(checkpoint);
  const recovered = await new RecorderCheckpointStore(adapter).load();
  assert.deepEqual(recovered, checkpoint);
  assert.throws(() => transition(checkpoint, "completed", clock), /Transição inválida/u);
});

test("checkpoint rejeita segredo em texto claro", () => {
  const event = rawEvent(1, "input", {
    secretReference: "secret.recorded.password",
    value: "não-deve-persistir"
  });
  const clock = { now: () => new Date("2026-08-17T18:00:00.000Z") };
  const checkpoint = {
    ...transition(createCheckpoint("Teste", "https://fixture.test", {
      captureScreenshots: false, captureSecrets: true, includeUploads: false
    }, clock), "recording", clock),
    events: [event],
    nextSequence: 2
  };
  assert.throws(() => assertNoPlaintextSecret(checkpoint), /texto claro/u);
});
