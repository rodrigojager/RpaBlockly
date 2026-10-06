export function initializeConfigurationUi({ fields, load, save, onMessage }) {
  const dialog = document.getElementById("configuration-dialog");
  const container = document.getElementById("configuration-fields");
  let documentValue = null;
  let renderedFields = [];

  document.getElementById("open-configuration").addEventListener("click", open);
  document.getElementById("save-configuration").addEventListener("click", persist);
  container.addEventListener("change", () => updateVisibility(container, renderedFields));

  async function open() {
    try {
      documentValue = structuredClone(await load());
      renderedFields = withSpyBrowserFields(fields());
      render(container, documentValue, renderedFields);
      dialog.showModal();
    } catch (error) {
      onMessage(error.message, true);
    }
  }

  async function persist() {
    try {
      if (documentValue === null) throw new Error("A configuração ainda não foi carregada.");
      const controls = [...container.querySelectorAll("[data-configuration-path]")];
      const values = controls.map(control => [control, readControl(control)]);
      validateSpySelections(container, values);
      for (const [control, value] of values) {
        setPath(documentValue, control.dataset.configurationPath, value);
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

function withSpyBrowserFields(fields) {
  const canonical = [
    { path: "Runtime.Browser", label: "Navegador", type: "select", defaultValue: "spybrowser", options: [
      { value: "spybrowser", label: "SpyBrowser" }, { value: "chromium", label: "Chromium Playwright" },
      { value: "cloakbrowser", label: "CloakBrowser" }, { value: "chrome", label: "Chrome" },
      { value: "chrome-beta", label: "Chrome Beta" }, { value: "chrome-dev", label: "Chrome Dev" },
      { value: "chrome-canary", label: "Chrome Canary" }, { value: "msedge", label: "Microsoft Edge" },
      { value: "msedge-beta", label: "Microsoft Edge Beta" }, { value: "msedge-dev", label: "Microsoft Edge Dev" },
      { value: "msedge-canary", label: "Microsoft Edge Canary" }, { value: "firefox", label: "Firefox" },
      { value: "webkit", label: "WebKit" }
    ] },
    { path: "Runtime.SpyBrowserHumanize", label: "Humanizar interações no SpyBrowser", type: "checkbox", defaultValue: true,
      visibleWhen: { path: "Runtime.Browser", equals: "spybrowser" },
      tooltip: "A humanização altera trajetórias do mouse; não muda a semântica nativa da ação nem garante evasão de CAPTCHA." },
    { path: "Runtime.SpyBrowserMouseAlgorithm", label: "Algoritmo de trajetória", type: "select", defaultValue: "bezier",
      options: [{ value: "bezier", label: "Bézier" }, { value: "cursory", label: "Cursory" }],
      visibleWhen: { path: "Runtime.Browser", equals: "spybrowser", and: { path: "Runtime.SpyBrowserHumanize", equals: true } },
      tooltip: "Bézier calcula trajetórias com curvas matemáticas. Cursory adapta formas de trajetórias gravadas com variação; muda o caminho do mouse, não a semântica nativa da ação." },
    { path: "Runtime.SpyBrowserCompatibilityMode", label: "Compatibilidade", type: "select", defaultValue: "legacy",
      options: [{ value: "legacy", label: "Legacy" }, { value: "playwrightcompatible", label: "PlaywrightCompatible" }],
      visibleWhen: { path: "Runtime.Browser", equals: "spybrowser", and: { path: "Runtime.SpyBrowserHumanize", equals: true } },
      tooltip: "Legacy preserva o comportamento existente. Compatible é recomendado para fluxos novos: usa Fill nativo em vez de digitação cadenciada, atalhos e duplo clique nativos, e rotas raw documentadas para opções não suportadas ou explícitas. Não oferece garantia contra CAPTCHA." }
  ];
  const canonicalPaths = new Set(canonical.map(field => field.path.toLowerCase()));
  const existing = new Map(fields.map(field => [field.path.toLowerCase(), field]));
  const merged = canonical.map(field => {
    const prior = existing.get(field.path.toLowerCase());
    return { ...prior, ...field, label: prior?.label ?? field.label, source: prior?.source ?? field.source,
      options: field.options ?? prior?.options };
  });
  const seen = new Set(canonicalPaths);
  const remainder = fields.filter(field => {
    const path = field.path.toLowerCase();
    if (seen.has(path)) return false;
    seen.add(path);
    return true;
  });
  return [...merged, ...remainder];
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
    const configuredValue = getPath(configuration, field.path);
    const spyDefaults = {
      "runtime.browser": "spybrowser",
      "runtime.spybrowserhumanize": true,
      "runtime.spybrowsermousealgorithm": "bezier",
      "runtime.spybrowsercompatibilitymode": "legacy"
    };
    const fallback = field.defaultValue ?? spyDefaults[field.path.toLowerCase()];
    const control = createControl(field, configuredValue === undefined ? fallback : configuredValue);
    control.dataset.configurationPath = field.path;
    control.dataset.configurationType = field.type;
    control.dataset.configurationNullable = String(field.nullable === true);
    label.append(control);
    if (field.tooltip) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "help-tip";
      button.textContent = "?";
      button.setAttribute("aria-label", `Ajuda: ${field.label}`);
      button.setAttribute("aria-describedby", `configuration-help-${field.path.replaceAll(".", "-")}`);
      const tip = document.createElement("span");
      tip.id = `configuration-help-${field.path.replaceAll(".", "-")}`;
      tip.className = "help-tip-content";
      tip.setAttribute("role", "tooltip");
      tip.textContent = field.tooltip;
      button.addEventListener("click", event => {
        event.preventDefault();
        event.stopPropagation();
        button.classList.toggle("is-open");
      });
      label.append(button, tip);
    }
    label.dataset.visibleWhen = field.visibleWhen ? JSON.stringify(field.visibleWhen) : "";
    container.append(label);
  }
  updateVisibility(container, fields);
}

function updateVisibility(container, fields) {
  for (const [index, field] of fields.entries()) {
    const row = container.children[index];
    const condition = field.visibleWhen;
    const control = row.querySelector("[data-configuration-path]");
    let visible = true;
    if (condition) {
      visible = getPathFromControls(container, condition.path) === condition.equals;
      if (condition.and) visible &&= getPathFromControls(container, condition.and.path) === condition.and.equals;
    }
    row.hidden = !visible;
    if (field.path.endsWith("SpyBrowserMouseAlgorithm") || field.path.endsWith("SpyBrowserCompatibilityMode")) control.disabled = !visible;
  }
}

function getPathFromControls(container, path) {
  const control = [...container.querySelectorAll("[data-configuration-path]")].find(item => item.dataset.configurationPath === path);
  if (!control) return undefined;
  return control.type === "checkbox" ? control.checked : control.value;
}

function createControl(field, value) {
  if (field.type.toLowerCase() === "stringlist") {
    const textarea = document.createElement("textarea");
    textarea.rows = 4;
    textarea.value = Array.isArray(value) ? value.join("\n") : "";
    return textarea;
  }

  if (field.type.toLowerCase() === "select") {
    const select = document.createElement("select");
    for (const optionData of field.options ?? []) {
      const option = document.createElement("option");
      option.value = optionData.value;
      option.textContent = optionData.label;
      select.append(option);
    }
    const restricted = ["runtime.browser", "runtime.spybrowsermousealgorithm", "runtime.spybrowsercompatibilitymode"].includes(field.path.toLowerCase());
    if (restricted) select.dataset.originalRestrictedValue = JSON.stringify(value);
    if (value === null && restricted) {
      const preserved = document.createElement("option");
      preserved.value = "__configuration_null__";
      preserved.textContent = "Inválido/não reconhecido (null)";
      preserved.dataset.preservedNull = "true";
      select.append(preserved);
      select.value = preserved.value;
    } else if (value !== null && value !== undefined) {
      const selected = String(value);
      const canonicalValue = field.path.toLowerCase() === "runtime.browser" ? selected.trim() : selected;
      const matchingOption = [...select.options].find(option => option.value.toLowerCase() === canonicalValue.toLowerCase());
      if (!matchingOption && restricted) {
        const preserved = document.createElement("option");
        preserved.value = selected;
        preserved.textContent = `Inválido/não reconhecido: ${selected}`;
        preserved.dataset.unrecognized = "true";
        select.append(preserved);
        select.value = selected;
      } else if (!matchingOption) {
        const preserved = document.createElement("option");
        preserved.value = selected;
        preserved.textContent = selected;
        select.append(preserved);
        select.value = selected;
      } else select.value = matchingOption.value;
    }
    return select;
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

function validateSpySelections(container, values) {
  const valueFor = path => values.find(([control]) =>
    control.dataset.configurationPath.toLowerCase() === path.toLowerCase())?.[1];
  const browser = valueFor("Runtime.Browser");
  const browserControl = values.find(([control]) => control.dataset.configurationPath.toLowerCase() === "runtime.browser")?.[0];
  if (!browserControl || browser === null || browser === undefined || browserControl.selectedOptions[0]?.dataset.unrecognized === "true" || browserControl.selectedOptions[0]?.dataset.preservedNull === "true") {
    throw new Error("Navegador inválido ou não reconhecido. Corrija a seleção antes de salvar.");
  }
  if (String(browser).trim().toLowerCase() !== "spybrowser") return;
  const humanize = valueFor("Runtime.SpyBrowserHumanize");
  if (humanize !== true) return;
  for (const path of ["Runtime.SpyBrowserMouseAlgorithm", "Runtime.SpyBrowserCompatibilityMode"]) {
    const control = values.find(([item]) => item.dataset.configurationPath.toLowerCase() === path.toLowerCase())?.[0];
    if (control && (control.selectedOptions[0]?.dataset.unrecognized === "true" || control.selectedOptions[0]?.dataset.preservedNull === "true")) {
      throw new Error(`${path} inválido ou não reconhecido. Corrija a seleção antes de salvar.`);
    }
  }
}

function readControl(control) {
  if (control.selectedOptions?.[0]?.dataset.preservedNull === "true") return null;
  if (control.tagName === "SELECT" && control.dataset.originalRestrictedValue !== undefined) {
    const original = JSON.parse(control.dataset.originalRestrictedValue);
    const originalValue = control.dataset.configurationPath.toLowerCase() === "runtime.browser"
      ? String(original).trim() : String(original);
    if (original !== null && original !== undefined && control.value.toLowerCase() === originalValue.toLowerCase()) return original;
  }
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
