(() => {
  "use strict";

  const config = window.RpaManualConfig || {};
  const catalog = window.RpaBlockCatalog || [];
  const root = document.documentElement;

  root.style.setProperty("--accent", config.accentColor || "#0f766e");
  root.style.setProperty("--secondary", config.secondaryColor || "#2563eb");
  const readStoredTheme = () => {
    try {
      return localStorage.getItem("rpa-doc-theme");
    } catch {
      return null;
    }
  };
  root.dataset.theme = readStoredTheme() || config.defaultTheme || "light";

  document.title = `${config.documentTitle || "Manual RPA"} — ${config.projectName || "RPA Blockly"}`;
  document.querySelectorAll("[data-project-name]").forEach(element => {
    element.textContent = config.projectName || "Base RPA Blockly";
  });
  document.querySelectorAll("[data-organization-name]").forEach(element => {
    element.textContent = config.organizationName || "Sua organização";
  });
  const support = document.querySelector("[data-support-text]");
  if (support) {
    support.textContent = config.supportText || "Defina o canal de suporte no arquivo manual.config.js.";
  }

  const escapeHtml = value => String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");

  const renderList = items => items.length
    ? `<ul>${items.map(item => `<li>${escapeHtml(item)}</li>`).join("")}</ul>`
    : "<p>Não há observações adicionais.</p>";

  const renderSteps = items => items.length
    ? `<ol class="execution-steps">${items.map(item => `<li>${escapeHtml(item)}</li>`).join("")}</ol>`
    : "<p>Este bloco não executa etapas adicionais.</p>";

  const formatLabels = {
    "data path": "caminho de dados, por exemplo input.campo",
    "runtime.*": "caminho temporário de saída, começando por runtime.",
    "CSS": "seletor CSS do elemento",
    "enum": "uma opção da lista apresentada",
    "enum interno": "preenchido automaticamente pelo editor",
    "JSON tipado": "texto, número, verdadeiro/falso, objeto ou lista",
    "lista JSON": "lista escrita no formato JSON",
    "lista de ações": "blocos encaixados dentro desta etapa"
  };

  const capabilityLabels = {
    web: "navegador",
    filesystem: "arquivos",
    oneTimeCode: "leitor de OTP",
    http: "requisição HTTP",
    safeFinalConfirmation: "proteção da ação final"
  };

  const renderPropertyRows = properties => properties
    .filter(item => config.showLegacyFields !== false || !item.label.toLowerCase().includes("legado"))
    .map(item => {
      const defaultText = item.defaultValue === "—"
        ? "Não há valor automático"
        : `Se você não preencher: ${item.defaultValue}`;
      const format = formatLabels[item.format] || item.format;
      return `
      <tr>
        <td><code>${escapeHtml(item.json)}</code><div>${escapeHtml(item.label)}</div></td>
        <td class="property-required">${escapeHtml(item.required)}</td>
        <td>${escapeHtml(format)}<div class="property-options">${escapeHtml(defaultText)}</div></td>
        <td>${escapeHtml(item.description)}${item.options.length ? `<div class="property-options"><strong>Opções aceitas:</strong> ${item.options.map(escapeHtml).join(" | ")}</div>` : ""}</td>
      </tr>`;
    })
    .join("");

  const renderBlock = (item, index) => {
    const id = `bloco-${item.blockType}`;
    const beginner = item.beginner || {
      plain: item.summary,
      scenario: "Use um exemplo do seu próprio processo para validar este bloco.",
      steps: [],
      success: "A etapa seguinte encontra o estado ou o valor esperado."
    };
    const capabilityBadges = item.capabilities
      .map(capability => `<span class="badge">Usa: ${escapeHtml(capabilityLabels[capability] || capability)}</span>`)
      .join("");
    const configuration = item.configuration?.length
      ? `<div class="callout"><strong>O que precisa estar configurado fora do bloco</strong>${renderList(item.configuration)}</div>`
      : "";
    return `
      <details class="block-card" id="${id}" data-index="${index}" data-category="${escapeHtml(item.category)}" data-search="${escapeHtml([
        item.title,
        item.blockType,
        item.actionType,
        item.category,
        item.summary,
        beginner.plain,
        beginner.scenario,
        beginner.success,
        ...beginner.steps,
        ...item.properties.map(property => `${property.json} ${property.label} ${property.description}`)
      ].join(" ").toLowerCase())}">
        <summary>
          <div>
            <h3>${escapeHtml(item.title)}</h3>
            <div class="block-meta">
              <span class="badge">${escapeHtml(item.category)}</span>
              <span class="badge type">Nome interno do bloco: ${escapeHtml(item.blockType)}</span>
              <span class="badge type">Nome salvo no JSON: ${escapeHtml(item.actionType)}</span>
              ${capabilityBadges}
            </div>
          </div>
        </summary>
        <div class="block-body">
          <div class="plain-explanation">
            <h4>O que este bloco faz</h4>
            <p>${escapeHtml(beginner.plain)}</p>
            <p><strong>Exemplo prático:</strong> ${escapeHtml(beginner.scenario)}</p>
          </div>
          <p class="block-summary"><strong>Resumo técnico:</strong> ${escapeHtml(item.summary)}</p>
          <div class="two-column">
            <div class="mini-panel"><h4>O que acontece durante a execução</h4>${renderSteps(beginner.steps)}</div>
            <div class="mini-panel"><h4>Como confirmar que funcionou</h4><p>${escapeHtml(beginner.success)}</p></div>
          </div>
          <div class="two-column">
            <div class="mini-panel"><h4>Quando escolher este bloco</h4>${renderList(item.useWhen)}</div>
            <div class="mini-panel"><h4>Quando escolher outro bloco</h4>${renderList(item.avoidWhen)}</div>
          </div>
          ${configuration}
          <h4>O que preencher em cada campo</h4>
          <p>Leia uma linha por vez. “Sim” significa que o campo não pode ficar vazio. “Um dos dois” significa que você escolhe entre escrever o valor diretamente ou indicar de onde o RPA deve lê-lo.</p>
          <table class="data-table">
            <thead><tr><th>Campo no JSON</th><th>Precisa preencher?</th><th>Que tipo de valor usar</th><th>Explicação</th></tr></thead>
            <tbody>${renderPropertyRows(item.properties)}</tbody>
          </table>
          <h4>Exemplo pronto para comparar com o seu JSON</h4>
          <pre><button class="copy-button" type="button">Copiar</button><code>${escapeHtml(JSON.stringify(item.example, null, 2))}</code></pre>
          <div class="two-column">
            <div class="mini-panel"><h4>Cuidados antes de usar</h4>${renderList(item.safety)}</div>
            <div class="mini-panel"><h4>Se der erro, verifique</h4>${renderList(item.failures)}</div>
          </div>
        </div>
      </details>`;
  };

  const container = document.getElementById("block-catalog");
  container.innerHTML = catalog.map(renderBlock).join("");

  const categories = [...new Set(catalog.map(item => item.category))]
    .sort((left, right) => left.localeCompare(right, "pt-BR"));
  const categorySelect = document.getElementById("block-category");
  categories.forEach(category => {
    const option = document.createElement("option");
    option.value = category;
    option.textContent = category;
    categorySelect.appendChild(option);
  });

  const searchInputs = [
    document.getElementById("global-search"),
    document.getElementById("block-search")
  ];
  const count = document.getElementById("block-count");
  const empty = document.getElementById("block-empty");

  const applyFilter = source => {
    const query = source.value.trim().toLowerCase();
    searchInputs.forEach(input => {
      if (input !== source) {
        input.value = source.value;
      }
    });
    const category = categorySelect.value;
    let visible = 0;
    document.querySelectorAll(".block-card").forEach(card => {
      const matchesText = !query || card.dataset.search.includes(query);
      const matchesCategory = !category || card.dataset.category === category;
      card.hidden = !(matchesText && matchesCategory);
      if (!card.hidden) {
        visible += 1;
        if (query) {
          card.open = true;
        }
      }
    });
    count.textContent = `${visible} de ${catalog.length} blocos`;
    empty.hidden = visible !== 0;
  };

  searchInputs.forEach(input => input.addEventListener("input", () => applyFilter(input)));
  categorySelect.addEventListener("change", () => applyFilter(searchInputs[1]));
  applyFilter(searchInputs[0]);

  document.getElementById("theme-toggle").addEventListener("click", () => {
    root.dataset.theme = root.dataset.theme === "dark" ? "light" : "dark";
    try {
      localStorage.setItem("rpa-doc-theme", root.dataset.theme);
    } catch {
      // Em file:// alguns navegadores bloqueiam storage; o tema ainda muda na sessão.
    }
  });

  document.getElementById("expand-blocks").addEventListener("click", event => {
    const visibleCards = [...document.querySelectorAll(".block-card:not([hidden])")];
    const shouldOpen = visibleCards.some(card => !card.open);
    visibleCards.forEach(card => {
      card.open = shouldOpen;
    });
    event.currentTarget.textContent = shouldOpen ? "Recolher visíveis" : "Expandir visíveis";
  });

  document.addEventListener("click", async event => {
    const button = event.target.closest(".copy-button");
    if (!button) {
      return;
    }
    const code = button.parentElement.querySelector("code").textContent;
    try {
      await navigator.clipboard.writeText(code);
      button.textContent = "Copiado";
      setTimeout(() => { button.textContent = "Copiar"; }, 1200);
    } catch {
      button.textContent = "Selecione o texto";
    }
  });

  document.querySelectorAll("pre").forEach(pre => {
    if (pre.querySelector(".copy-button")) {
      return;
    }
    const button = document.createElement("button");
    button.className = "copy-button";
    button.type = "button";
    button.textContent = "Copiar";
    pre.prepend(button);
  });

  const staticLinks = [...document.querySelectorAll("main > section[id], main > header[id]")]
    .map(section => ({ id: section.id, title: section.dataset.toc || section.querySelector("h1, h2")?.textContent }))
    .filter(item => item.title);
  const staticToc = document.getElementById("toc-static");
  staticToc.innerHTML = staticLinks
    .map(item => `<a href="#${escapeHtml(item.id)}">${escapeHtml(item.title)}</a>`)
    .join("");
  const blockToc = document.getElementById("toc-blocks");
  blockToc.innerHTML = catalog
    .map(item => `<a href="#bloco-${escapeHtml(item.blockType)}">${escapeHtml(item.title)}</a>`)
    .join("");

  const observer = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (!entry.isIntersecting) {
        return;
      }
      document.querySelectorAll(".sidebar a").forEach(link => link.classList.remove("active"));
      document.querySelector(`.sidebar a[href="#${entry.target.id}"]`)?.classList.add("active");
    });
  }, { rootMargin: "-20% 0px -70% 0px" });
  document.querySelectorAll("main section[id], main header[id], .block-card[id]")
    .forEach(section => observer.observe(section));
})();
