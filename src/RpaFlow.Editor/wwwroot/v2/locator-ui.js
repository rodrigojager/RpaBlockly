import { editorState, updateState } from "./state.js";

export function initializeLocatorUi(onChanged, onError = () => {}) {
  const search = document.getElementById("locator-search");
  const list = document.getElementById("locator-list");
  const editor = document.getElementById("locator-json");
  const title = document.getElementById("locator-editor-title");

  function render() {
    const query = search.value.trim().toLowerCase();
    list.replaceChildren();
    const locators = editorState.package?.locators?.locators ?? [];
    for (const locator of locators.filter(item =>
      !query || item.id.toLowerCase().includes(query) ||
      item.displayName.toLowerCase().includes(query))) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "locator-list-item secondary";
      button.dataset.locatorId = locator.id;
      button.textContent = `${locator.displayName} · ${locator.id}`;
      button.addEventListener("click", () => select(locator.id));
      list.append(button);
    }
  }

  function select(id) {
    const locator = (editorState.package?.locators?.locators ?? [])
      .find(item => item.id.toLowerCase() === id.toLowerCase());
    if (!locator) return;
    updateState({ selectedLocatorId: locator.id });
    title.textContent = `Locator: ${locator.displayName}`;
    editor.value = JSON.stringify(locator, null, 2);
  }

  search.addEventListener("input", render);
  document.getElementById("new-locator").addEventListener("click", () => {
    const existing = new Set((editorState.package?.locators?.locators ?? [])
      .map(item => item.id.toLowerCase()));
    let suffix = 1;
    while (existing.has(`novo-locator-${suffix}`)) suffix += 1;
    const id = `novo-locator-${suffix}`;
    editor.value = JSON.stringify({
      id,
      displayName: "Novo locator",
      candidates: [{
        id: `${id}-original`,
        origin: "developer",
        developerRole: "original",
        originalOrder: 0,
        recipe: {
          frames: [],
          target: { strategy: "css", selector: "#elemento" }
        }
      }],
      fingerprints: []
    }, null, 2);
    updateState({ selectedLocatorId: id });
    title.textContent = "Novo locator";
  });
  document.getElementById("save-locator-draft").addEventListener("click", () => {
    try {
      const value = JSON.parse(editor.value);
      validateLocator(value);
      const packageValue = structuredClone(editorState.package);
      const values = packageValue.locators.locators;
      const previousId = editorState.selectedLocatorId;
      const index = values.findIndex(item =>
        item.id.toLowerCase() === String(previousId ?? "").toLowerCase());
      const duplicate = values.findIndex((item, candidateIndex) =>
        candidateIndex !== index && item.id.toLowerCase() === value.id.toLowerCase());
      if (duplicate >= 0) throw new Error(`Já existe o locator '${value.id}'.`);
      if (index >= 0) values[index] = value;
      else values.push(value);
      updateState({ package: packageValue, selectedLocatorId: value.id });
      title.textContent = `Locator: ${value.displayName}`;
      render();
      onChanged();
    } catch (error) {
      onError(error);
    }
  });

  return { render, select };
}

function validateLocator(locator) {
  if (!locator || Array.isArray(locator) || typeof locator !== "object") {
    throw new Error("O locator deve ser um objeto JSON.");
  }
  if (!/^[A-Za-z][A-Za-z0-9._-]*$/.test(locator.id ?? "")) {
    throw new Error("O ID do locator é inválido.");
  }
  if (!String(locator.displayName ?? "").trim()) {
    throw new Error("O nome amigável do locator é obrigatório.");
  }
  if (!Array.isArray(locator.candidates) || locator.candidates.length === 0) {
    throw new Error("O locator exige ao menos um candidato.");
  }
  if (!locator.candidates[0]?.recipe?.target) {
    throw new Error("O candidato principal exige recipe.target.");
  }
}
