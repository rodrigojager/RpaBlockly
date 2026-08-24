import type { EncryptedSecretEnvelope } from "../core/types.js";

export class EncryptedSecretStore {
  public constructor(private readonly databaseName = "rpablockly-recorder-v1") {}

  public async put(envelope: EncryptedSecretEnvelope): Promise<void> {
    await this.withStore("readwrite", (store) => store.put(envelope));
  }

  public async list(): Promise<EncryptedSecretEnvelope[]> {
    return await this.withStore("readonly", (store) => store.getAll()) as EncryptedSecretEnvelope[];
  }

  public async clear(): Promise<void> {
    await this.withStore("readwrite", (store) => store.clear());
  }

  private async withStore(
    mode: IDBTransactionMode,
    operation: (store: IDBObjectStore) => IDBRequest
  ): Promise<unknown> {
    const database = await this.open();
    return await new Promise((resolve, reject) => {
      const transaction = database.transaction("secrets", mode);
      const request = operation(transaction.objectStore("secrets"));
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error ?? new Error("Falha no IndexedDB."));
      transaction.oncomplete = () => database.close();
    });
  }

  private async open(): Promise<IDBDatabase> {
    return await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains("evidence")) {
          request.result.createObjectStore("evidence", { keyPath: "id" });
        }
        if (!request.result.objectStoreNames.contains("secrets")) {
          request.result.createObjectStore("secrets", { keyPath: "reference" });
        }
        if (!request.result.objectStoreNames.contains("uploads")) {
          request.result.createObjectStore("uploads", { keyPath: "key" });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error ?? new Error("Falha ao abrir IndexedDB."));
    });
  }
}
