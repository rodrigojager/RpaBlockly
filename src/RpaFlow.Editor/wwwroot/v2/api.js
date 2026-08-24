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

export async function inspectRecorderBundle(file) {
  const response = await fetch("/api/recorder/imports/inspect", {
    method: "POST",
    cache: "no-store",
    headers: {
      ...headers(),
      "Content-Type": "application/octet-stream",
      "X-File-Name": file.name
    },
    body: await file.arrayBuffer()
  });
  return readResponse(response);
}

export function getRecorderImport(stagingId, stagingToken) {
  return recorderRequest(`/api/recorder/imports/${encodeURIComponent(stagingId)}`, stagingToken);
}

export function validateRecorderImport(stagingId, stagingToken, decision) {
  return recorderRequest(
    `/api/recorder/imports/${encodeURIComponent(stagingId)}/validate`,
    stagingToken,
    { method: "POST", body: JSON.stringify(decision) });
}

export function applyRecorderImport(stagingId, stagingToken, decision) {
  return recorderRequest(
    `/api/recorder/imports/${encodeURIComponent(stagingId)}/apply`,
    stagingToken,
    { method: "POST", body: JSON.stringify(decision) });
}

export async function deleteRecorderImport(stagingId, stagingToken) {
  const response = await fetch(`/api/recorder/imports/${encodeURIComponent(stagingId)}`, {
    method: "DELETE",
    cache: "no-store",
    headers: { ...headers(), "X-Recorder-Staging-Token": stagingToken }
  });
  if (!response.ok && response.status !== 404) await readResponse(response);
}

export async function recorderEvidence(stagingId, stagingToken, evidenceId, thumbnail = false) {
  const response = await fetch(
    `/api/recorder/imports/${encodeURIComponent(stagingId)}/evidence/` +
      `${encodeURIComponent(evidenceId)}?thumbnail=${thumbnail}`,
    {
      cache: "no-store",
      headers: { ...headers(), "X-Recorder-Staging-Token": stagingToken }
    });
  if (!response.ok) await readResponse(response);
  return response.blob();
}

export function startAssistedExecution(document) {
  return request("/api/assisted-executions", {
    method: "POST",
    body: JSON.stringify(document)
  });
}

export function getAssistedExecution(executionId, afterSequence = 0) {
  return request(
    `/api/assisted-executions/${encodeURIComponent(executionId)}` +
      `?after=${encodeURIComponent(afterSequence)}`);
}

export async function getLatestAssistedExecution() {
  const response = await fetch("/api/assisted-executions/latest", {
    cache: "no-store",
    headers: headers()
  });
  if (response.status === 404) return null;
  return readResponse(response);
}

export function stopAssistedExecution(executionId) {
  return request(
    `/api/assisted-executions/${encodeURIComponent(executionId)}/stop`,
    { method: "POST" });
}

export async function assistedEvidence(executionId, evidenceId) {
  const response = await fetch(
    `/api/assisted-executions/${encodeURIComponent(executionId)}/evidence/` +
      encodeURIComponent(evidenceId),
    { cache: "no-store", headers: headers() });
  if (!response.ok) await readResponse(response);
  return response.blob();
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

async function recorderRequest(path, stagingToken, options = {}) {
  return request(path, {
    ...options,
    headers: { ...(options.headers ?? {}), "X-Recorder-Staging-Token": stagingToken }
  });
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
