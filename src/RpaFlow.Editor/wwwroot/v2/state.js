const listeners = new Set();

export const editorState = {
  session: null,
  package: null,
  selectedBlock: null,
  selectedLocatorId: null,
  conflict: null
};

export function updateState(values) {
  Object.assign(editorState, values);
  for (const listener of listeners) listener(editorState);
}

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function clone(value) {
  return structuredClone(value);
}
