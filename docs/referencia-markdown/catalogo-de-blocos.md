# Catálogo de blocos e ações

Este catálogo descreve o estado atual da toolbox compartilhada: **35 blocos visuais** e **32 valores distintos de `action.type`**.

A diferença existe porque:

- **Se valor** e **Se elemento** geram `type: "if"`;
- **Download após clique** e **Download por requisição** geram `type: "download"`;
- **Definir subfluxo** gera uma entrada em `subflows`, não uma ação.

Todos os RPAs recebem a mesma toolbox. A sequência e a configuração do JSON tornam cada automação específica.

## Convenções comuns

Toda ação possui:

| Campo | JSON | Regra |
| --- | --- | --- |
| Nome | `name` | Descrição operacional obrigatória. |
| ID | `id` | Único no documento inteiro; preservado em `block.data`. |
| Tipo | `type` | Determinado pelo bloco; não é texto livre na interface. |

Neste documento:

- **valor** significa exatamente um entre `value` e `valueSource`;
- **localizador** significa `selector` e, quando necessário, `scope`, textos e `frameSelectors`;
- **destino runtime** significa `target` obrigatório em `runtime.*`, sem índice de array;
- **destino de artefato** é o conjunto de pasta, arquivo, separação, conflito e `target` opcional.

## Localizador comum

| JSON | Uso |
| --- | --- |
| `selector` | Seletor CSS do alvo. |
| `scope` | Contêiner opcional. |
| `scopeHasText` ou `scopeHasTextSource` | Filtra o contêiner antes de procurar o alvo. |
| `hasText` ou `hasTextSource` | Filtra o alvo. |
| `frameSelectors` | Até 8 iframes, do externo para o interno. |

Os pares literal/source são exclusivos. Ações singulares exigem um alvo único quando executadas. Não use `.First` ou seletor ambíguo para esconder duplicidade.

## Resumo dos 32 tipos

| Bloco Blockly | `action.type` | Handler | Capabilities |
| --- | --- | --- | --- |
| `rpa_navigate` | `navigate` | Navegação | `web` |
| `rpa_click` | `click` | Navegação | `web` |
| `rpa_click_optional` | `clickIfVisible` | Navegação | `web` |
| `rpa_wait` | `wait` | Navegação | `web` |
| `rpa_click_new_page` | `clickAndSwitchPage` | Navegação | `web` |
| `rpa_switch_page` | `switchPage` | Navegação | `web` |
| `rpa_close_page` | `closePage` | Navegação | `web` |
| `rpa_wait_stable` | `waitStable` | Navegação | `web` |
| `rpa_fill` | `fill` | Formulário | `web` |
| `rpa_select_option` | `selectOption` | Formulário | `web` |
| `rpa_set_checked` | `setChecked` | Formulário | `web` |
| `rpa_press_key` | `pressKey` | Formulário | `web` |
| `rpa_type_sequentially` | `typeSequentially` | Formulário | `web` |
| `rpa_type_across_inputs` | `typeAcrossInputs` | Formulário | `web` |
| `rpa_upload` | `upload` | Formulário | `web`, `filesystem` |
| `rpa_preserve_fill` | `preserveOrFill` | Formulário | `web` |
| `rpa_select2` | `select2` | Formulário | `web` |
| `rpa_currency` | `fillMaskedCurrency` | Formulário | `web` |
| `rpa_fail` | `fail` | Dados e artefatos | nenhuma |
| `rpa_transform_path` | `transformPath` | Dados e artefatos | nenhuma |
| `rpa_set_variable` | `setVariable` | Dados e artefatos | nenhuma |
| `rpa_capture_timestamp` | `captureTimestamp` | Dados e artefatos | nenhuma |
| `rpa_wait_one_time_code` | `waitForOneTimeCode` | Dados e artefatos | `oneTimeCode` |
| `rpa_read_element` | `readElement` | Dados e artefatos | `web` |
| `rpa_read_elements` | `readElements` | Dados e artefatos | `web` |
| `rpa_screenshot` | `screenshot` | Dados e artefatos | `web`, `filesystem` |
| `rpa_download_click` ou `rpa_download_request` | `download` | Dados e artefatos | `web`, `filesystem`; `http` no modo request |
| `rpa_safe_final` | `safeFinalConfirmation` | Dados e artefatos | `web`, `safeFinalConfirmation` |
| `rpa_if_value` ou `rpa_if_element` | `if` | Controle | nenhuma |
| `rpa_repeat` | `repeat` | Controle | nenhuma |
| `rpa_for_each` | `forEach` | Controle | nenhuma |
| `rpa_run_subflow` | `runSubflow` | Controle | nenhuma |

Capabilities são metadados do catálogo e dos testes. No estado atual, o host não faz auditoria prévia nem enforcement automático por capability; elas não filtram blocos por RPA, não concedem autorização e não substituem o handler ou a política específica exigida.

## Navegação, cliques e abas

### Navegar — `navigate`

- **Obrigatório:** URL em `value` ou `valueSource`.
- **Opcional:** `timeoutMs`.
- **Efeito:** chama `GotoAsync` e aguarda `DOMContentLoaded`.
- **Editor atual:** o bloco visual informa uma origem como `input.url`; o runtime também aceita literal.

### Clicar — `click`

- **Obrigatório:** localizador.
- **Efeito:** exige exatamente um elemento visível e clica.
- **Uso:** ação comum; não substitui confirmação final protegida.

### Clicar se visível — `clickIfVisible`

- **Obrigatório:** localizador.
- **Opcional:** `timeoutMs`, padrão 2.000 ms.
- **Efeito:** se não ficar visível no prazo, registra e continua; se aparecer, exige unicidade e clica.
- **Uso:** somente para estado realmente opcional. Outros erros não são ignorados.

### Aguardar elemento — `wait`

- **Obrigatório:** localizador e `state`.
- **Estados:** `visible`, `attached`, `hidden`, `detached`.
- **Opcional:** `optional`, `timeoutMs`, `matchMode`.
- **Defaults do runtime:** `optional: false`; `matchMode: "first"` quando omitido.
- **Novo bloco:** usa `single` como padrão visual.
- **Efeito:** espera estado observável; somente timeout é tolerado quando `optional` for `true`. `single` recusa mais de um alvo; exige um para `visible`/`attached`, zero para `detached` e zero ou um oculto para `hidden`.

### Clicar e assumir nova aba — `clickAndSwitchPage`

- **Obrigatório:** localizador e `readySelector` da nova aba.
- **Opcional:** `timeoutMs`.
- **Efeito:** arma a espera de página antes do clique, aguarda `DOMContentLoaded`, troca a página atual e valida o seletor inicial.

### Assumir aba existente — `switchPage`

- **Obrigatório:** valor esperado, `property` e `comparison`.
- **`property`:** `url` ou `title`.
- **`comparison`:** `exact`, `caseInsensitive` ou `contains`.
- **Opcional:** `readySelector`.
- **Efeito:** exige exatamente uma aba correspondente, traz para frente e troca o contexto do fluxo.

### Fechar aba atual — `closePage`

- **Opcional:** `readySelector` da aba que permanecerá.
- **Efeito:** fecha a atual, assume a última aba restante e valida o seletor opcional.
- **Proteção:** falha se a aba atual for a única do contexto.

### Aguardar página estável — `waitStable`

- **Campos específicos:** nenhum além de ID e nome.
- **Efeito:** aguarda período configurado sem atividade de rede, ausência de indicadores visíveis de loading e estabilidade do formulário.
- **Configuração:** `Runtime.ReadinessQuietPeriodMs`, `Runtime.FormStabilityMs` e `Runtime.BusySelectors`.

## Formulários e anexos

### Preencher — `fill`

- **Obrigatório:** localizador e valor.
- **Efeito:** exige um elemento visível e usa preenchimento padrão do Playwright.
- **Uso:** apenas quando o RPA é responsável pelo valor. Se a página puder preenchê-lo, use `preserveOrFill`.

### Selecionar opção nativa — `selectOption`

- **Obrigatório:** localizador, valor e `optionMode`.
- **`optionMode`:** `value`, `label` ou `index`.
- **Efeito:** seleciona em um `<select>` nativo e falha quando nenhuma opção corresponde.
- **Índice:** inteiro maior ou igual a zero.

### Definir marcação — `setChecked`

- **Obrigatório:** localizador e valor booleano.
- **Efeito:** marca ou desmarca checkbox/radio e confirma o estado final.

### Pressionar tecla — `pressKey`

- **Obrigatório:** localizador e tecla/combinação Playwright como valor.
- **Exemplos:** `Tab`, `Enter`, `Control+A`.
- **Efeito:** exige um elemento visível e pressiona a tecla.

Use esse bloco separado quando a saída do campo for uma etapa independente. Um fluxo de autenticação pode compor digitação e `Tab` em duas ações.

### Digitar sequencialmente — `typeSequentially`

- **Obrigatório:** localizador e valor.
- **Opcional:** `delayMs`, `clearFirst`, `blurAfter`.
- **Defaults do runtime para JSON antigo:** 50 ms, `false`, `false`.
- **Defaults de um bloco novo:** 50 ms, `true`, `true`.
- **Efeito:** opcionalmente limpa, digita caractere a caractere, opcionalmente sai com `Tab`, relocaliza o controle e confirma o valor final exato.

Use quando o componente depende de eventos reais de teclado ou impede colagem. Não use como tentativa genérica de contornar validação.

### Digitar em inputs segmentados — `typeAcrossInputs`

- **Obrigatório:** localizador e valor.
- **Opcional:** `delayMs`, `clearFirst`, `blurAfter`.
- **Efeito:** considera somente os inputs visíveis do localizador, exige exatamente um input por elemento de texto, digita um elemento em cada campo com eventos reais de teclado e confirma a concatenação final.
- **Cardinalidade:** é intencionalmente múltipla; `matchMode` não se aplica e é rejeitado.
- **Uso:** códigos de uso único, PINs e componentes equivalentes que dividem um valor entre vários campos.

O handler relocaliza os campos ao longo da interação para tolerar componentes que recriam o DOM ou avançam o foco. O valor não é escrito no console.

### Anexar e aguardar estabilidade — `upload`

- **Obrigatório:** seletor do input file e caminho em valor/source.
- **Opcional:** `optional`, padrão `false`.
- **Efeito:** resolve caminho absoluto ou relativo à pasta da configuração, verifica existência, usa `SetInputFilesAsync` e aguarda readiness.
- **Editor atual:** o bloco visual usa uma origem como `attachments.pdf`; o runtime também aceita literal.

### Preservar ou preencher — `preserveOrFill`

- **Obrigatório:** localizador, valor e `comparison`.
- **Comparações:** `exact`, `caseInsensitive`, `currency`.
- **Efeito:** preenche quando vazio; preserva quando equivalente; falha quando a página já contém valor diferente.

### Selecionar opção Select2 — `select2`

- **Obrigatório:** `selector` do select nativo, `triggerSelector`, `optionSelector` e valor.
- **Opcional:** `comparison` como `exact`, `caseInsensitive` ou `numeric`.
- **Compatibilidade:** omissão mantém a comparação legada dos fluxos atuais.
- **Efeito:** preserva valor existente equivalente ou abre o controle visível e clica na opção renderizada.

Não consulte o endpoint interno nem injete `<option>` no DOM.

### Preencher campo monetário — `fillMaskedCurrency`

- **Obrigatório:** localizador e valor.
- **Opcional:** `decimalPlaces` de 0 a 6, `delayMs` de 0 a 1.000, `commitKey` `Tab` ou `Enter`.
- **Defaults:** 2 casas, 30 ms, `Tab`.
- **Efeito:** preserva valor numericamente equivalente ou digita unidades menores para a máscara da página formatar e depois valida o resultado.

## Dados e leituras

### Interromper com erro — `fail`

- **Obrigatório:** mensagem em valor/source.
- **Efeito:** encerra imediatamente a execução sem clicar ou produzir efeito remoto.
- **Uso:** estados terminais detectados, como autenticação recusada ou pré-condição ausente.

### Transformar caminho — `transformPath`

- **Obrigatório:** caminho em valor/source, `operation` e destino runtime.
- **Operações:** `fileName`, `fileNameWithoutExtension`, `extension`, `directoryName`.
- **Efeito:** transforma apenas o texto, sem acessar o sistema de arquivos. Aceita caminhos locais e UNC.

### Definir variável — `setVariable`

- **Obrigatório:** valor/source e destino runtime.
- **Efeito:** copia texto, número, booleano, nulo, array ou objeto para `runtime.*`.
- **Proteção:** não altera `input`, `config` ou `attachments`.

### Capturar instante UTC — `captureTimestamp`

- **Obrigatório:** destino runtime.
- **Efeito:** grava o instante UTC atual no formato round-trip `O` do .NET.
- **Uso:** cria um marco temporal explícito antes de solicitar um token ou iniciar outra espera correlacionada.
- **Capability:** nenhuma; a ação não acessa serviço externo.

### Aguardar código de uso único — `waitForOneTimeCode`

- **Obrigatório:** `providerAlias`, `notBeforeSource`, destino runtime, `timeoutMs` e `pollIntervalMs`.
- **Alias:** começa por letra ASCII e aceita letras, números, ponto, hífen e sublinhado.
- **Marco temporal:** `notBeforeSource` precisa apontar para um timestamp round-trip, normalmente produzido por `captureTimestamp`.
- **Limites:** timeout de 1.000 a 600.000 ms; polling de 500 a 60.000 ms e nunca maior que o timeout.
- **Efeito:** delega a espera a um `IOneTimeCodeProvider` injetado pelo host e grava somente o código em `runtime.*`.
- **Proteção:** falha claramente quando o host não configurou o provider. A ação não implementa login, reenvio do token nem nova tentativa de autenticação.

### Ler elemento — `readElement`

- **Obrigatório:** localizador, `property` e destino runtime.
- **Propriedades:** `value`, `text`, `checked`, `attribute`.
- **Quando `attribute`:** informe também `attribute`.
- **Efeito:** exige um elemento anexado e grava o valor tipado em `runtime.*`.

### Ler vários elementos — `readElements`

- **Obrigatório:** localizador, `property` e destino runtime.
- **Opcional:** `maxItems`, padrão 1.000, faixa 1 a 10.000.
- **Efeito:** lê zero ou mais elementos em ordem e grava um array. Falha antes da leitura se a quantidade ultrapassar o limite.

## Arquivos, evidências e segurança

### Salvar screenshot — `screenshot`

- **Obrigatório:** somente ID, tipo e nome; o editor solicita um nome de arquivo para blocos novos.
- **Opcional:** destino de artefato e `target`.
- **Fallback do runtime:** `evidencia` quando o arquivo não é informado.
- **Extensão:** sem extensão, acrescenta `.png`; extensões explícitas aceitas são `.png`, `.jpg` e `.jpeg`.
- **Efeito:** captura a página inteira e publica o caminho somente depois de salvar.

`screenshotName` continua aceito como fallback legado em `screenshot` e `safeFinalConfirmation`, inclusive em um fluxo legado. O editor o converte para `fileName` ao reserializar; use `fileName` em novos fluxos.

### Download após clique — `download` com `downloadMode: "click"`

- **Obrigatório:** localizador e `downloadMode`.
- **Opcional:** `timeoutMs`, destino e nome; sem nome, usa o sugerido pelo site.
- **Novo bloco:** timeout visual padrão de 30.000 ms.
- **Efeito:** arma a espera de download antes do clique e persiste o arquivo.

### Download por requisição — `download` com `downloadMode: "request"`

- **Obrigatório:** URL em valor/source, `method` `GET` ou `POST` e `downloadMode`.
- **Corpo opcional:** `requestBody` ou `requestBodySource`.
- **`bodyType`:** `json`, `text` ou `form`; padrão de execução `json`.
- **Cabeçalhos opcionais:** objeto `requestHeaders` ou `requestHeadersSource`.
- **Saída:** destino comum, nome sugerido/literal/source e `target` opcional.
- **Efeito:** usa o contexto HTTP associado ao navegador, inclusive cookies.

POST pode produzir efeito remoto. Inclua somente quando o efeito estiver entendido e autorizado.

### Confirmação final segura — `safeFinalConfirmation`

- **Obrigatório:** localizador e uma `IPagePolicyFactory` específica que implemente a proteção.
- **Comprovação opcional e atômica:** quando um dos campos abaixo for informado, todos são obrigatórios:
  - `successSelector` e `successText` identificam exatamente uma mensagem visível de sucesso;
  - `protocolSelector` delimita o texto que contém o protocolo;
  - `protocolPattern` extrai um único valor pelo grupo nomeado `protocol`;
  - `completionTarget`, `confirmationMessageTarget` e `protocolTarget` são destinos `runtime.*` distintos.
- **Opcional:** `timeoutMs`, destino de screenshot e `target` do caminho da evidência.
- **Invariantes:** no máximo uma; sempre a última ação principal; proibida em condição, loop e subfluxo.
- **Efeito seguro:** a política captura a evidência, arma a proteção, reconhece o alerta esperado e cancela.
- **Efeito autorizado pelo host:** a política valida toda a configuração antes do clique, aceita o alerta, exige resposta HTTP bem-sucedida, comprova a mensagem, extrai o protocolo e só então grava a conclusão.

A caixa **comprovar conclusão e publicar feedback** do Blockly representa essa escolha. Marcada, o editor grava e valida o conjunto completo; desmarcada, omite os sete campos e preserva o comportamento seguro legado. Ela não habilita o envio.

A presença desses campos no JSON não concede autorização para enviar. A política padrão recusa essa ação, e o host seguro continua cancelando o alerta. Nunca substitua por `click` para facilitar um teste.

## Controle de fluxo

### Se valor e Se elemento — `if`

Os blocos `rpa_if_value` e `rpa_if_element` geram o mesmo tipo. `condition.type` escolhe o avaliador.

**Valor:**

- lados literal ou source;
- operadores `equals`, `notEquals`, `contains`, `notContains`, `startsWith`, `endsWith`, `matchesRegex`, `isEmpty`, `isNotEmpty`;
- `ignoreCase`, padrão `false`.

**Elemento:**

- localizador e estado `visible`, `attached`, `hidden` ou `detached`;
- `matchMode` `first` ou `single`;
- avaliação imediata, sem espera.

Em `single`, mais de um alvo é sempre inválido; `visible`/`attached` exigem um alvo, `detached` exige zero e `hidden` aceita zero ou um alvo oculto.

Pelo menos um entre `actions` e `elseActions` precisa ser não vazio.

### Repetir — `repeat`

- **Obrigatório:** exatamente um entre `times` e `timesSource`; `actions` não vazio.
- **Opcional:** `indexVariable`, padrão `repeatIndex`.
- **Efeito:** executa de 0 a 1.000.000 vezes e expõe índice iniciado em zero em `loop.*`.

### Para cada item — `forEach`

- **Obrigatório:** exatamente um entre `items` e `itemsSource`; `itemVariable`; `actions` não vazio.
- **Opcional:** `indexVariable`, padrão `<itemVariable>Index`.
- **Efeito:** percorre arrays, empilha item e índice em `loop.*` e preserva escopos externos em loops aninhados.

### Executar subfluxo — `runSubflow`

- **Obrigatório:** `subflow`.
- **Efeito:** localiza a definição sem diferenciar maiúsculas/minúsculas e executa suas ações pelo mesmo dispatcher.
- **Validação:** referência existente, ausência de ciclo e profundidade máxima de 32.

### Definir subfluxo — sem `action.type`

- **Blockly:** `rpa_subflow_definition`.
- **JSON:** chave em `subflows` contendo lista de ações.
- **Layout:** permanece como bloco raiz separado.
- **Regra:** nome válido e único; pelo menos uma ação.

## Destino comum de artefatos

| Campo visual | JSON | Default |
| --- | --- | --- |
| Pasta | `destinationDirectory` ou `destinationDirectorySource` | `Runtime.OutputDirectory` |
| Arquivo | `fileName` ou `fileNameSource` | depende da ação |
| Separar por execução | `separateByExecution` | `true` |
| Conflito | `conflictStrategy` | `unique` |
| Salvar caminho | `target` | ausente |

`unique` cria nome livre, `fail` recusa colisão e `overwrite` substitui deliberadamente. Literais e sources correspondentes são exclusivos.

## Atualizações recentes

Os nove tipos adicionados à biblioteca original são:

- `selectOption`;
- `setChecked`;
- `pressKey`;
- `readElements`;
- `switchPage`;
- `closePage`;
- `captureTimestamp`;
- `waitForOneTimeCode`;
- `typeAcrossInputs`.

Também foram generalizados, sem criar novos tipos:

- cardinalidade `matchMode` em espera e condição de elemento;
- comparação configurável no Select2;
- casas, intervalo e tecla de confirmação em campo monetário;
- nome do índice em `repeat`;
- literais JSON tipados em condições;
- readiness por `BusySelectors`, `ReadinessQuietPeriodMs` e `FormStabilityMs`;
- loops aninhados e subfluxos com round-trip e execução local.

Propriedades omitidas mantêm a semântica legada documentada no [Schema versão 1](flow-schema-v1.md#defaults-compatíveis).
