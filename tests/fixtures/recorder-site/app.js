const query = new URLSearchParams(location.search);
if (query.get("changed") === "1") {
  document.querySelector("#dynamic-action")?.removeAttribute("data-testid");
}
document.querySelector("#cadastro")?.addEventListener("submit", (event) => {
  event.preventDefault();
  document.querySelector("#resultado").textContent = "Formulário enviado";
});
document.querySelector("#spa-next")?.addEventListener("click", () => {
  history.pushState({ fixture: true }, "", "/app");
  document.querySelector("#resultado").textContent = "SPA atualizada";
});
document.querySelector("#dynamic-action")?.addEventListener("click", () => {
  document.querySelector("#resultado").textContent = "Ação dinâmica concluída";
});
document.querySelector("[data-testid='scope-primary'] button")?.addEventListener("click", () => {
  document.querySelector("#resultado").textContent = "Escopo correto selecionado";
});
const shadowHost = document.querySelector("#shadow-host");
if (shadowHost instanceof HTMLElement) {
  const shadow = shadowHost.attachShadow({ mode: "open" });
  const button = document.createElement("button");
  button.type = "button";
  button.dataset.testid = "shadow-action";
  button.textContent = "Confirmar no shadow DOM";
  button.addEventListener("click", () => {
    document.querySelector("#resultado").textContent = "Shadow DOM confirmado";
  });
  shadow.append(button);
}
document.querySelector("#open-popup")?.addEventListener("click", () => {
  window.open("/popup.html", "fixture-popup", "width=480,height=320");
});
