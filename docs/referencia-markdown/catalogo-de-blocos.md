# Catálogo de blocos V2

O catálogo compilado possui 33 tipos de ação e 36 blocos. `download` e `if` têm
duas variantes visuais; `rpa_subflow_definition` não é uma ação.

| Bloco | Ação | Locators | Observação |
| --- | --- | --- | --- |
| `rpa_navigate` | `navigate` | — | URL literal ou `valueSource`. |
| `rpa_click` | `click` | target | Exige alvo singular. |
| `rpa_click_optional` | `clickIfVisible` | target | Ausência recuperável, pacote inválido não é ignorado. |
| `rpa_wait` | `wait` | target | Estado attached/detached/visible/hidden. |
| `rpa_fill` | `fill` | target | Valor literal ou source. |
| `rpa_select_option` | `selectOption` | target | Modo value/label/index. |
| `rpa_set_checked` | `setChecked` | target | Booleano literal ou source. |
| `rpa_press_key` | `pressKey` | target | Tecla literal ou source. |
| `rpa_type_sequentially` | `typeSequentially` | target | Digitação com delay limitado. |
| `rpa_type_across_inputs` | `typeAcrossInputs` | target many | Um caractere por input visível. |
| `rpa_click_new_page` | `clickAndSwitchPage` | target, ready | Aguarda nova página e prontidão. |
| `rpa_upload` | `upload` | target | Usa caminho autorizado de attachment/config. |
| `rpa_wait_stable` | `waitStable` | — | Rede, formulário e busy selectors. |
| `rpa_preserve_fill` | `preserveOrFill` | target | Preserva valor compatível. |
| `rpa_select2` | `select2` | target, trigger, options many | Interação composta com lista. |
| `rpa_currency` | `fillMaskedCurrency` | target | Casas decimais e commit Tab/Enter. |
| `rpa_fail` | `fail` | — | Encerra com mensagem resolvida. |
| `rpa_transform_path` | `transformPath` | — | Transforma caminho e grava `output`. |
| `rpa_capture_timestamp` | `captureTimestamp` | — | Instante UTC em `runtime.*`. |
| `rpa_wait_one_time_code` | `waitForOneTimeCode` | — | Provider, janela e polling obrigatórios. |
| `rpa_complete_authentication_attempt` | `completeAuthenticationAttempt` | — | Marcador idempotente que libera somente a cerca de retry do login. |
| `rpa_set_variable` | `setVariable` | — | Valor em `runtime.*`. |
| `rpa_read_element` | `readElement` | target | value/text/checked/attribute. |
| `rpa_read_elements` | `readElements` | target many | Coleção limitada em `runtime.*`. |
| `rpa_switch_page` | `switchPage` | ready | Seleciona página por URL/título. |
| `rpa_close_page` | `closePage` | ready | Fecha página e escolhe a remanescente. |
| `rpa_download_click` | `download` | target | Variante `downloadMode: click`. |
| `rpa_download_request` | `download` | — | GET/POST, headers/body literal ou source. |
| `rpa_screenshot` | `screenshot` | target opcional | Página ou elemento. |
| `rpa_safe_final` | `safeFinalConfirmation` | target, success, protocol | Mantém a semântica genérica já existente. |
| `rpa_if_value` | `if` | — | Condição de valores. |
| `rpa_if_element` | `if` | condition | Condição de estado de elemento. |
| `rpa_repeat` | `repeat` | — | Contagem literal ou source. |
| `rpa_for_each` | `forEach` | — | Lista literal ou source; `loop.*`. |
| `rpa_run_subflow` | `runSubflow` | — | Chama subfluxo existente e acíclico. |
| `rpa_subflow_definition` | — | — | Contêiner visual de subfluxo. |

## Propriedades comuns

- `id`, `type` e `name` são obrigatórios;
- valores literal/source são mutuamente exclusivos;
- `output` deve usar `runtime.*`;
- `timeoutMs`, `pollIntervalMs`, `delayMs` e loops possuem limites;
- ações web usam `LocatorUseDefinition`, nunca selector;
- `actions` e `elseActions` preservam ordem e IDs globais únicos.

O contrato normativo está em `schemas/flow-v2.schema.json` e no
`FlowDefinitionValidator`. O teste de baseline deriva a cobertura diretamente do
`FlowActionCatalog`.
