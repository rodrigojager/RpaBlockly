export function initializeConfigurationUi({ fields, load, save, onMessage }) {
  const dialog = document.getElementById("configuration-dialog");
  const container = document.getElementById("configuration-fields");
  let documentValue = null;

  document.getElementById("open-configuration").addEventListener("click", open);
  document.getElementById("save-configuration").addEventListener("click", persist);

  async function open() {
    try {
      documentValue = structuredClone(await load());
      render(container, documentValue, fields());
      dialog.showModal();
    } catch (error) {
      onMessage(error.message, true);
    }
  }

  async function persist() {
    try {
      if (documentValue === null) throw new Error("A configuração ainda não foi carregada.");
      for (const control of container.querySelectorAll("[data-configuration-path]")) {
        setPath(
          documentValue,
          control.dataset.configurationPath,
          readControl(control));
      }
      await save(documentValue);
      onMessage("Configuração salva sem edição manual de JSON.");
      dialog.close();
    } catch (error) {
      onMessage(error.message, true);
    }
  }

  return { open };
}

function render(container, configuration, fields) {
  container.replaceChildren();
  if (fields.length === 0) {
    const empty = document.createElement("p");
    empty.className = "empty-variables";
    empty.textContent = "Este perfil não expõe campos de configuração editáveis.";
    container.append(empty);
    return;
  }

  for (const field of fields) {
    const label = document.createElement("label");
    label.className = "configuration-field";
    label.append(document.createTextNode(field.label));
    if (field.source) {
      const source = document.createElement("code");
      source.textContent = field.source;
      label.append(source);
    }
    const control = createControl(field, getPath(configuration, field.path));
    control.dataset.configurationPath = field.path;
    control.dataset.configurationType = field.type;
    control.dataset.configurationNullable = String(field.nullable === true);
    label.append(control);
    container.append(label);
  }
}

function createControl(field, value) {
  if (field.type.toLowerCase() === "stringlist") {
    const textarea = document.createElement("textarea");
    textarea.rows = 4;
    textarea.value = Array.isArray(value) ? value.join("\n") : "";
    return textarea;
  }

  const input = document.createElement("input");
  const type = field.type.toLowerCase();
  input.type = type === "checkbox" ? "checkbox" : type === "number" ? "number" :
    ["url", "email", "password", "date"].includes(type) ? type : "text";
  if (input.type === "checkbox") input.checked = value === true;
  else if (value !== null && value !== undefined) input.value = String(value);
  if (input.type === "password") input.autocomplete = "new-password";
  return input;
}

function readControl(control) {
  const type = control.dataset.configurationType.toLowerCase();
  const nullable = control.dataset.configurationNullable === "true";
  if (type === "checkbox") return control.checked;
  if (type === "number") {
    if (control.value.trim() === "" && nullable) return null;
    if (control.value.trim() === "" || !Number.isFinite(control.valueAsNumber)) {
      throw new Error(`${control.dataset.configurationPath} exige um número.`);
    }
    return control.valueAsNumber;
  }
  if (type === "stringlist") {
    if (control.value.trim() === "" && nullable) return null;
    return control.value.split(/\r?\n/u).map(value => value.trim()).filter(Boolean);
  }
  if (control.value === "" && nullable) return null;
  return control.value;
}

function getPath(owner, path) {
  let current = owner;
  for (const segment of path.split(".")) {
    if (current === null || typeof current !== "object" || Array.isArray(current)) return undefined;
    const key = Object.keys(current).find(candidate =>
      candidate.localeCompare(segment, undefined, { sensitivity: "accent" }) === 0);
    if (key === undefined) return undefined;
    current = current[key];
  }
  return current;
}

function setPath(owner, path, value) {
  const segments = path.split(".");
  let current = owner;
  for (let index = 0; index < segments.length - 1; index += 1) {
    const segment = segments[index];
    const key = Object.keys(current).find(candidate =>
      candidate.localeCompare(segment, undefined, { sensitivity: "accent" }) === 0) ?? segment;
    if (current[key] === null || typeof current[key] !== "object" || Array.isArray(current[key])) {
      current[key] = {};
    }
    current = current[key];
  }
  const finalSegment = segments.at(-1);
  const finalKey = Object.keys(current).find(candidate =>
    candidate.localeCompare(finalSegment, undefined, { sensitivity: "accent" }) === 0) ?? finalSegment;
  current[finalKey] = value;
}
