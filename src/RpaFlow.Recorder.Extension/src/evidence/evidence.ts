import type { EvidenceItem, EvidenceMask } from "../../../../schemas/generated/contracts.js";
import { stableId } from "../core/stable.js";

export const maximumEvidenceItems = 200;
export const maximumEvidenceBytes = 5 * 1024 * 1024;
export const maximumEvidenceDimension = 4_096;

export interface EvidenceAsset {
  metadata: EvidenceItem;
  image: Uint8Array;
  thumbnail: Uint8Array;
}

export class ScreenshotRateLimiter {
  private lastCaptureAt = Number.NEGATIVE_INFINITY;

  public constructor(private readonly minimumIntervalMs = 600) {}

  public tryAcquire(nowMs: number): boolean {
    if (nowMs - this.lastCaptureAt < this.minimumIntervalMs) return false;
    this.lastCaptureAt = nowMs;
    return true;
  }
}

export async function createEvidenceAsset(
  dataUrl: string,
  eventId: string,
  actionId: string,
  capturedAtUtc: string,
  masks: EvidenceMask[]
): Promise<EvidenceAsset> {
  const image = await decodeAndRedact(dataUrl, masks, maximumEvidenceDimension);
  const thumbnail = await decodeAndRedact(dataUrl, masks, 480);
  if (image.bytes.length > maximumEvidenceBytes) throw new Error("Evidência excede 5 MiB.");
  const id = stableId("evidence", eventId, actionId, capturedAtUtc);
  return {
    metadata: {
      id,
      eventId,
      actionId,
      kind: "after",
      path: `evidence/${id}.webp`,
      thumbnailPath: `evidence/thumbnails/${id}.webp`,
      mimeType: "image/webp",
      width: image.width,
      height: image.height,
      byteLength: image.bytes.length,
      capturedAtUtc,
      masks
    },
    image: image.bytes,
    thumbnail: thumbnail.bytes
  };
}

export function removeEvidence(assets: ReadonlyArray<EvidenceAsset>, id: string): EvidenceAsset[] {
  return assets.filter((asset) => asset.metadata.id !== id);
}

export function slideshowItems(assets: ReadonlyArray<EvidenceAsset>) {
  return [...assets]
    .sort((left, right) => left.metadata.capturedAtUtc.localeCompare(right.metadata.capturedAtUtc))
    .map((asset) => ({
      id: asset.metadata.id,
      imagePath: asset.metadata.path,
      thumbnailPath: asset.metadata.thumbnailPath,
      alt: `Evidência visual da ação ${asset.metadata.actionId}`,
      interactive: false as const
    }));
}

async function decodeAndRedact(
  dataUrl: string,
  masks: EvidenceMask[],
  maximumDimension: number
): Promise<{ bytes: Uint8Array; width: number; height: number }> {
  const blob = await (await fetch(dataUrl)).blob();
  const bitmap = await createImageBitmap(blob);
  const scale = Math.min(1, maximumDimension / Math.max(bitmap.width, bitmap.height));
  const width = Math.max(1, Math.round(bitmap.width * scale));
  const height = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = new OffscreenCanvas(width, height);
  const context = canvas.getContext("2d", { alpha: false });
  if (context === null) throw new Error("Canvas 2D indisponível para mascarar evidência.");
  context.drawImage(bitmap, 0, 0, width, height);
  context.fillStyle = "#111827";
  for (const mask of masks) {
    context.fillRect(mask.x * scale, mask.y * scale, mask.width * scale, mask.height * scale);
  }
  bitmap.close();
  const result = await canvas.convertToBlob({ type: "image/webp", quality: 0.82 });
  return { bytes: new Uint8Array(await result.arrayBuffer()), width, height };
}

export class EvidenceStore {
  public constructor(private readonly databaseName = "rpablockly-recorder-v1") {}

  public async put(asset: EvidenceAsset): Promise<void> {
    await this.withStore("readwrite", (store) => store.put({
      id: asset.metadata.id,
      metadata: asset.metadata,
      image: asset.image,
      thumbnail: asset.thumbnail
    }));
  }

  public async list(): Promise<EvidenceAsset[]> {
    return await this.withStore("readonly", (store) => store.getAll()) as EvidenceAsset[];
  }

  public async delete(id: string): Promise<void> {
    await this.withStore("readwrite", (store) => store.delete(id));
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
      const transaction = database.transaction("evidence", mode);
      const request = operation(transaction.objectStore("evidence"));
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
