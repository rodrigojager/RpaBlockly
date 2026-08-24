import type { RecorderCheckpoint, SessionStorageAdapter } from "./types.js";

export const recorderCheckpointKey = "rpablockly.recorder.checkpoint.v1";

export class RecorderCheckpointStore {
  public constructor(private readonly storage: SessionStorageAdapter) {}

  public async load(): Promise<RecorderCheckpoint | undefined> {
    const value = await this.storage.get<RecorderCheckpoint>(recorderCheckpointKey);
    if (value === undefined) return undefined;
    validateCheckpoint(value);
    return structuredClone(value);
  }

  public async save(checkpoint: RecorderCheckpoint): Promise<void> {
    validateCheckpoint(checkpoint);
    assertNoPlaintextSecret(checkpoint);
    await this.storage.set(recorderCheckpointKey, structuredClone(checkpoint));
  }

  public async clear(): Promise<void> {
    await this.storage.remove(recorderCheckpointKey);
  }
}

export class ChromeSessionStorage implements SessionStorageAdapter {
  public async get<T>(key: string): Promise<T | undefined> {
    const result = await chrome.storage.session.get(key);
    return result[key] as T | undefined;
  }

  public async set<T>(key: string, value: T): Promise<void> {
    await chrome.storage.session.set({ [key]: value });
  }

  public async remove(key: string): Promise<void> {
    await chrome.storage.session.remove(key);
  }
}

export function validateCheckpoint(checkpoint: RecorderCheckpoint): void {
  if (checkpoint.schemaVersion !== 1 || !checkpoint.sessionId || !checkpoint.startedAtUtc) {
    throw new Error("Checkpoint do Recorder inválido.");
  }
  if (checkpoint.events.length > 100_000 || checkpoint.nextSequence < 1) {
    throw new Error("Checkpoint excede os limites da sessão.");
  }
  if (checkpoint.events.some((event) => event.sequence >= checkpoint.nextSequence)) {
    throw new Error("Checkpoint possui sequência inconsistente.");
  }
}

export function assertNoPlaintextSecret(checkpoint: RecorderCheckpoint): void {
  for (const event of checkpoint.events) {
    if (event.secretReference !== undefined && event.value !== undefined) {
      throw new Error("Checkpoint não pode persistir segredo em texto claro.");
    }
    if (event.target?.attributes !== undefined &&
        Object.keys(event.target.attributes).some((name) => /password|secret|token|value/iu.test(name))) {
      throw new Error("Checkpoint contém atributo sensível.");
    }
  }
}
