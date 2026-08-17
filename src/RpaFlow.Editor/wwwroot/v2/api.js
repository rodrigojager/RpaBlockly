import { editorState, updateState } from "./state.js";

export async function connect() {
  const response = await fetch("/api/session", { cache: "no-store" });
  if (!response.ok) throw new Error("O microservidor do editor não respondeu.");
  const session = await response.json();
  updateState({ session });
  return session;
}

export async function openPackage() {
  const value = await fetchPackage();
  updateState({ package: value, conflict: null });
  return value;
}

export function fetchPackage() {
  return request("/api/package");
}

export async function savePackage(documents) {
  const current = editorState.package;
  if (!current) throw new Error("Nenhum pacote está aberto.");
  const response = await fetch("/api/package", {
    method: "PUT",
    headers: headers(),
    body: JSON.stringify({ expectedRevision: current.revision, ...documents })
  });
  if (response.status === 409) {
    const problem = await response.json();
    updateState({ conflict: problem.error ?? "A revisão aberta ficou obsoleta." });
    throw new RevisionConflictError(problem.error);
  }
  const value = await readResponse(response);
  updateState({ package: value, conflict: null });
  return value;
}

export async function readConfiguration() {
  return request("/api/configuration");
}

export async function saveConfiguration(configuration) {
  return request("/api/configuration", {
    method: "PUT",
    body: JSON.stringify(configuration)
  });
}

export class RevisionConflictError extends Error {
  constructor(message) {
    super(message || "A revisão do pacote mudou. Recarregue e compare antes de salvar.");
    this.name = "RevisionConflictError";
  }
}

async function request(path, options = {}) {
  const response = await fetch(path, {
    cache: "no-store",
    ...options,
    headers: { ...headers(), ...(options.headers ?? {}) }
  });
  return readResponse(response);
}

async function readResponse(response) {
  const value = await response.json();
  if (!response.ok) throw new Error(value.error ?? `Falha HTTP ${response.status}.`);
  return value;
}

function headers() {
  const result = { "Content-Type": "application/json" };
  if (editorState.session?.token) result["X-Editor-Token"] = editorState.session.token;
  return result;
}
