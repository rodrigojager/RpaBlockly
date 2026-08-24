let locatorProvider = () => [];

export function setLocatorProvider(provider) {
  locatorProvider = provider;
}

export class FieldLocatorReference extends Blockly.FieldDropdown {
  constructor(value = "") {
    super(function options() {
      const locators = locatorProvider();
      const values = locators
        .map(locator => [locator.displayName || locator.id, locator.id]);
      const current = this.getValue();
      if (current && !values.some(([, id]) => id === current)) {
        values.unshift([`⚠ ${current}`, current]);
      }
      return values.length ? values : [["Nenhum locator disponível", ""]];
    });
    if (value) this.setValue(value);
  }

  showEditor_() {
    showLocatorPicker(this.getValue()).then(value => {
      if (value !== null) this.setValue(value);
    });
  }
}

function showLocatorPicker(current) {
  const locators = locatorProvider();
  return new Promise(resolve => {
    const dialog = document.createElement("dialog");
    dialog.className = "locator-picker";
    const heading = document.createElement("h2");
    heading.textContent = "Selecionar locator";
    const search = document.createElement("input");
    search.type = "search";
    search.placeholder = "Pesquisar por ID ou nome";
    search.autofocus = true;
    const list = document.createElement("div");
    list.className = "locator-list";
    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.className = "secondary";
    cancel.textContent = "Cancelar";
    dialog.append(heading, search, list, cancel);
    document.body.append(dialog);

    const finish = value => {
      dialog.close();
      dialog.remove();
      resolve(value);
    };
    const render = () => {
      const query = search.value.trim().toLowerCase();
      list.replaceChildren();
      for (const locator of locators.filter(item =>
        !query || item.id.toLowerCase().includes(query) ||
        String(item.displayName ?? "").toLowerCase().includes(query))) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "locator-list-item secondary";
        button.textContent = `${locator.displayName || locator.id} · ${locator.id}`;
        if (locator.id === current) button.dataset.current = "true";
        button.addEventListener("click", () => finish(locator.id));
        list.append(button);
      }
    };
    search.addEventListener("input", render);
    cancel.addEventListener("click", () => finish(null));
    dialog.addEventListener("cancel", event => {
      event.preventDefault();
      finish(null);
    });
    render();
    dialog.showModal();
  });
}
