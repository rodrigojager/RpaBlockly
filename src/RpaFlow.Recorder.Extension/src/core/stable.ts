const sensitiveQueryNames = /^(?:access_token|api[_-]?key|auth|code|credential|jwt|password|secret|session|signature|sig|token)$/iu;
const sensitiveAttributeNames = /^(?:value|password|passwd|secret|token|authorization|cookie|set-cookie|session|api[-_]?key)$/iu;

export function canonicalJson(value: unknown): string {
  return `${JSON.stringify(sortValue(value), undefined, 2)}\n`;
}

export function stableId(prefix: string, ...parts: unknown[]): string {
  const input = canonicalJson(parts);
  let hash = 0xcbf29ce484222325n;
  for (const byte of new TextEncoder().encode(input)) {
    hash ^= BigInt(byte);
    hash = BigInt.asUintN(64, hash * 0x100000001b3n);
  }
  return `${prefix}-${hash.toString(16).padStart(16, "0")}`;
}

export function slug(value: string, fallback = "item"): string {
  const normalized = value.normalize("NFKD").replace(/[\u0300-\u036f]/gu, "")
    .toLowerCase().replace(/[^a-z0-9]+/gu, "-").replace(/^-|-$/gu, "").slice(0, 60);
  return normalized || fallback;
}

export function sanitizeText(value: string | undefined, maximum = 2_000): string | undefined {
  if (value === undefined) return undefined;
  const normalized = value.replace(/\s+/gu, " ").trim();
  return normalized.length === 0 ? undefined : normalized.slice(0, maximum);
}

export function sanitizeUrl(raw: string): { url: string; removedSensitiveQuery: boolean } {
  try {
    const parsed = new URL(raw);
    parsed.username = "";
    parsed.password = "";
    parsed.hash = "";
    let removedSensitiveQuery = false;
    const safe = [...parsed.searchParams.entries()]
      .filter(([name]) => {
        const allowed = !sensitiveQueryNames.test(name);
        removedSensitiveQuery ||= !allowed;
        return allowed;
      })
      .sort(([leftName, leftValue], [rightName, rightValue]) =>
        leftName.localeCompare(rightName) || leftValue.localeCompare(rightValue));
    parsed.search = "";
    for (const [name, value] of safe) parsed.searchParams.append(name, value);
    return { url: parsed.toString(), removedSensitiveQuery };
  } catch {
    return { url: "about:blank", removedSensitiveQuery: true };
  }
}

export function sanitizeAttributes(source: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(source)
    .filter(([name, value]) => !sensitiveAttributeNames.test(name) && !looksSensitive(value))
    .sort(([left], [right]) => left.localeCompare(right))
    .slice(0, 64)
    .map(([name, value]) => [name, sanitizeText(value, 300) ?? ""]));
}

export function looksSensitive(value: string): boolean {
  return /(?:bearer\s+|-----BEGIN [A-Z ]*PRIVATE KEY-----|(?:password|passwd|secret|token|api[-_]?key)\s*[:=])/iu.test(value);
}

export async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const buffer = await crypto.subtle.digest("SHA-256", bytes as BufferSource);
  return [...new Uint8Array(buffer)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function sortValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortValue);
  if (value !== null && typeof value === "object" && !(value instanceof Uint8Array)) {
    return Object.fromEntries(Object.entries(value as Record<string, unknown>)
      .filter(([, child]) => child !== undefined)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, child]) => [key, sortValue(child)]));
  }
  return value;
}
