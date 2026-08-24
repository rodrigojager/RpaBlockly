import { sha256Hex, slug } from "../core/stable.js";
import type { UploadCapture } from "../core/types.js";

export const maximumUploadBytes = 20 * 1024 * 1024;
export const maximumTotalUploadBytes = 50 * 1024 * 1024;
const blockedExtensions = new Set(["exe", "dll", "com", "bat", "cmd", "ps1", "js", "mjs", "html", "htm"]);

export async function captureUpload(file: File, includeContent: boolean): Promise<UploadCapture> {
  if (file.size > maximumUploadBytes) throw new Error("O arquivo excede o limite de 20 MiB.");
  const extension = file.name.split(".").pop()?.toLowerCase() ?? "";
  if (blockedExtensions.has(extension)) throw new Error("O tipo de arquivo não pode ser incluído.");
  const bytes = new Uint8Array(await file.arrayBuffer());
  try {
    return {
      name: sanitizeFileName(file.name),
      mimeType: file.type || "application/octet-stream",
      size: file.size,
      sha256: await sha256Hex(bytes),
      included: includeContent,
      ...(includeContent ? { contentBase64: bytesToBase64(bytes) } : {})
    };
  } finally {
    bytes.fill(0);
  }
}

export function validateUploadTotals(uploads: ReadonlyArray<UploadCapture>): void {
  const total = uploads.filter((upload) => upload.included).reduce((sum, upload) => sum + upload.size, 0);
  if (total > maximumTotalUploadBytes) throw new Error("Uploads excedem o limite total de 50 MiB.");
}

function sanitizeFileName(name: string): string {
  const extension = name.includes(".") ? `.${slug(name.split(".").pop() ?? "bin", "bin")}` : "";
  const stem = name.includes(".") ? name.slice(0, name.lastIndexOf(".")) : name;
  return `${slug(stem, "arquivo")}${extension}`.slice(0, 180);
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let index = 0; index < bytes.length; index += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(index, index + 0x8000));
  }
  return btoa(binary);
}
