import type { UploadCapture } from "../core/types.js";

interface StoredUpload {
  key: string;
  upload: UploadCapture;
}

export class UploadStore {
  public constructor(private readonly databaseName = "rpablockly-recorder-v1") {}

  public async put(upload: UploadCapture): Promise<void> {
    await this.withStore("readwrite", (store) => store.put({ key: uploadKey(upload), upload }));
  }

  public async list(): Promise<UploadCapture[]> {
    const records = await this.withStore("readonly", (store) => store.getAll()) as StoredUpload[];
    return records.map((record) => record.upload);
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
      const transaction = database.transaction("uploads", mode);
      const request = operation(transaction.objectStore("uploads"));
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

export function hydrateUploads<T extends { events: Array<{ upload?: UploadCapture }> }>(
  checkpoint: T,
  uploads: ReadonlyArray<UploadCapture>
): T {
  const byKey = new Map(uploads.map((upload) => [uploadKey(upload), upload]));
  return {
    ...checkpoint,
    events: checkpoint.events.map((event) => event.upload === undefined
      ? event
      : { ...event, upload: byKey.get(uploadKey(event.upload)) ?? event.upload })
  };
}

function uploadKey(upload: UploadCapture): string {
  return upload.sha256 ?? `${upload.name}:${upload.size}`;
}
