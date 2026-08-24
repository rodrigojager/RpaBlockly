export const actionCatalog = [
  entry("navigate", "rpa_navigate", "Navegar", "Navegação", []),
  entry("click", "rpa_click", "Clicar", "Navegação", ["target"]),
  entry("clickIfVisible", "rpa_click_optional", "Clicar se visível", "Navegação", ["target"]),
  entry("wait", "rpa_wait", "Aguardar elemento", "Esperas", ["target"]),
  entry("fill", "rpa_fill", "Preencher", "Formulários", ["target"]),
  entry("selectOption", "rpa_select_option", "Selecionar opção", "Formulários", ["target"]),
  entry("setChecked", "rpa_set_checked", "Definir marcação", "Formulários", ["target"]),
  entry("pressKey", "rpa_press_key", "Pressionar tecla", "Formulários", ["target"]),
  entry("typeSequentially", "rpa_type_sequentially", "Digitar sequencialmente", "Formulários", ["target"]),
  entry("typeAcrossInputs", "rpa_type_across_inputs", "Digitar em campos", "Formulários", ["target"]),
  entry("clickAndSwitchPage", "rpa_click_new_page", "Clicar e trocar página", "Navegação", ["target", "ready"]),
  entry("upload", "rpa_upload", "Enviar arquivo", "Formulários", ["target"]),
  entry("waitStable", "rpa_wait_stable", "Aguardar estabilidade", "Esperas", []),
  entry("preserveOrFill", "rpa_preserve_fill", "Preservar ou preencher", "Formulários", ["target"]),
  entry("select2", "rpa_select2", "Selecionar em Select2", "Formulários", ["target", "trigger", "options"]),
  entry("fillMaskedCurrency", "rpa_currency", "Preencher moeda", "Formulários", ["target"]),
  entry("fail", "rpa_fail", "Falhar execução", "Dados e controle", []),
  entry("transformPath", "rpa_transform_path", "Transformar caminho", "Dados e controle", []),
  entry("captureTimestamp", "rpa_capture_timestamp", "Capturar instante", "Dados e controle", []),
  entry("waitForOneTimeCode", "rpa_wait_one_time_code", "Aguardar código de uso único", "Esperas", []),
  entry("completeAuthenticationAttempt", "rpa_complete_authentication_attempt", "Concluir tentativa de autenticação", "Dados e controle", []),
  entry("setVariable", "rpa_set_variable", "Definir variável", "Dados e controle", []),
  entry("readElement", "rpa_read_element", "Ler elemento", "Leitura", ["target"]),
  entry("readElements", "rpa_read_elements", "Ler elementos", "Leitura", ["target"]),
  entry("switchPage", "rpa_switch_page", "Trocar página", "Navegação", ["ready"]),
  entry("closePage", "rpa_close_page", "Fechar página", "Navegação", ["ready"]),
  variant("download", "rpa_download_click", "Baixar por clique", "Arquivos e evidências", ["target"], "click"),
  variant("download", "rpa_download_request", "Baixar por requisição", "Arquivos e evidências", [], "request"),
  entry("screenshot", "rpa_screenshot", "Capturar screenshot", "Arquivos e evidências", ["target"]),
  entry("safeFinalConfirmation", "rpa_safe_final", "Confirmação final", "Arquivos e evidências", ["target", "success", "protocol"]),
  control("if", "rpa_if_value", "Se valor", "Controle", [], "value"),
  control("if", "rpa_if_element", "Se elemento", "Controle", ["condition"], "element"),
  control("repeat", "rpa_repeat", "Repetir", "Controle", []),
  control("forEach", "rpa_for_each", "Para cada", "Controle", []),
  entry("runSubflow", "rpa_run_subflow", "Executar subfluxo", "Subfluxos", []),
  { actionType: null, blockType: "rpa_subflow_definition", label: "Definir subfluxo", category: "Subfluxos", roles: [], structural: "subflow" }
];

export const actionTypes = Object.freeze(
  [...new Set(actionCatalog.filter(item => item.actionType).map(item => item.actionType))]);

export function definitionForAction(action) {
  if (action.type === "download") {
    return actionCatalog.find(item =>
      item.actionType === "download" && item.variant === (action.downloadMode ?? "click"));
  }
  if (action.type === "if") {
    return actionCatalog.find(item =>
      item.actionType === "if" && item.variant === (action.condition?.type ?? "value"));
  }
  return actionCatalog.find(item => item.actionType === action.type);
}

export function definitionForBlock(blockType) {
  return actionCatalog.find(item => item.blockType === blockType);
}

function entry(actionType, blockType, label, category, roles) {
  return { actionType, blockType, label, category, roles, structural: "action" };
}

function variant(actionType, blockType, label, category, roles, name) {
  return { ...entry(actionType, blockType, label, category, roles), variant: name };
}

function control(actionType, blockType, label, category, roles, variantName) {
  return {
    ...entry(actionType, blockType, label, category, roles),
    structural: actionType,
    variant: variantName
  };
}
