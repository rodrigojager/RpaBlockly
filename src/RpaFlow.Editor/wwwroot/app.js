(() => {
  "use strict";

  if (!window.Blockly) {
    document.body.innerHTML = "<p>Não foi possível carregar o Blockly local. Verifique a pasta vendor/blockly.</p>";
    return;
  }

  const blocks = [
    {
      type: "rpa_navigate",
      message0: "navegar: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Abrir o portal" }],
      message1: "origem da URL %1",
      args1: [{ type: "field_input", name: "VALUE_SOURCE", text: "input.url" }],
      previousStatement: null,
      nextStatement: null,
      colour: 205,
      tooltip: "Abre uma URL e aguarda DOMContentLoaded."
    },
    {
      type: "rpa_click",
      message0: "clicar: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Clicar no elemento" }],
      message1: "seletor CSS %1",
      args1: [{ type: "field_input", name: "SELECTOR", text: "#elemento" }],
      message2: "escopo opcional %1",
      args2: [{ type: "field_input", name: "SCOPE", text: "" }],
      message3: "texto contido opcional %1",
      args3: [{ type: "field_input", name: "HAS_TEXT", text: "" }],
      previousStatement: null,
      nextStatement: null,
      colour: 205,
      tooltip: "Aguarda um único elemento visível e clica."
    },
    {
      type: "rpa_click_optional",
      message0: "clicar se visível: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Fechar elemento opcional" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: ".elemento" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "texto contido %1 timeout (ms) %2",
      args2: [
        { type: "field_input", name: "HAS_TEXT", text: "" },
        { type: "field_number", name: "TIMEOUT", value: 2000, min: 100, max: 600000 }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 205,
      tooltip: "Clica somente quando o elemento opcional aparecer."
    },
    {
      type: "rpa_wait",
      message0: "aguardar elemento: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Aguardar elemento" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#elemento" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "texto contido opcional %1",
      args2: [{ type: "field_input", name: "HAS_TEXT", text: "" }],
      message3: "estado %1 opcional %2 timeout (0 = padrão) %3",
      args3: [
        {
          type: "field_dropdown",
          name: "STATE",
          options: [
            ["visível", "visible"],
            ["anexado ao DOM", "attached"],
            ["oculto", "hidden"],
            ["removido do DOM", "detached"]
          ]
        },
        { type: "field_checkbox", name: "OPTIONAL", checked: false },
        { type: "field_number", name: "TIMEOUT", value: 0, min: 0, max: 600000 }
      ],
      message4: "correspondência do seletor %1",
      args4: [{
        type: "field_dropdown",
        name: "MATCH_MODE",
        options: [
          ["exatamente um elemento", "single"],
          ["primeiro elemento (compatibilidade)", "first"]
        ]
      }],
      previousStatement: null,
      nextStatement: null,
      colour: 45,
      tooltip: "Aguarda o estado configurado sem pausa fixa."
    },
    {
      type: "rpa_fill",
      message0: "preencher: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Preencher campo" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#campo" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "valor %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.valor" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Preenche um input com um valor literal ou da configuração."
    },
    {
      type: "rpa_select_option",
      message0: "selecionar opção nativa: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Selecionar opção" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "select" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "localizar opção por %1 valor %2 %3",
      args2: [
        {
          type: "field_dropdown",
          name: "OPTION_MODE",
          options: [["value", "value"], ["texto exibido", "label"], ["índice", "index"]]
        },
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.opcao" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Seleciona uma opção de um select HTML por value, texto exibido ou índice."
    },
    {
      type: "rpa_set_checked",
      message0: "definir marcação: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Marcar ou desmarcar" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "input[type='checkbox']" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "valor booleano %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["booleano literal", "json"], ["da configuração", "source"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "true" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Marca ou desmarca checkbox/radio e confirma o estado final."
    },
    {
      type: "rpa_press_key",
      message0: "pressionar tecla no elemento: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Pressionar tecla" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#campo" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "tecla Playwright %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["literal", "literal"], ["da configuração", "source"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "Enter" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Pressiona uma tecla ou combinação Playwright em um único elemento visível."
    },
    {
      type: "rpa_type_sequentially",
      message0: "digitar sequencialmente: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Digitar no campo" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#campo" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "valor %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.valor" }
      ],
      message3: "intervalo por tecla (ms) %1 limpar antes %2 sair com Tab %3",
      args3: [
        { type: "field_number", name: "DELAY", value: 50, min: 0, max: 1000 },
        { type: "field_checkbox", name: "CLEAR_FIRST", checked: true },
        { type: "field_checkbox", name: "BLUR_AFTER", checked: true }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Gera teclas em sequência, sem colar, e confirma que o valor permaneceu no campo."
    },
    {
      type: "rpa_type_across_inputs",
      message0: "digitar entre vários inputs: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Digitar código segmentado" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#codigo input" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "valor %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.codigo" }
      ],
      message3: "intervalo por caractere (ms) %1 limpar antes %2 sair com Tab %3",
      args3: [
        { type: "field_number", name: "DELAY", value: 50, min: 0, max: 1000 },
        { type: "field_checkbox", name: "CLEAR_FIRST", checked: true },
        { type: "field_checkbox", name: "BLUR_AFTER", checked: true }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Distribui os caracteres entre inputs visíveis e confirma o valor completo."
    },
    {
      type: "rpa_click_new_page",
      message0: "clicar e assumir nova aba: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Abrir nova aba" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#botao" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "texto contido %1 elemento inicial da nova aba %2",
      args2: [
        { type: "field_input", name: "HAS_TEXT", text: "" },
        { type: "field_input", name: "READY_SELECTOR", text: "#elemento" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 205,
      tooltip: "Clica, espera uma nova aba e passa o restante do fluxo para ela."
    },
    {
      type: "rpa_switch_page",
      message0: "assumir aba existente: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Assumir aba" }],
      message1: "comparar %1 usando %2",
      args1: [
        {
          type: "field_dropdown",
          name: "PROPERTY",
          options: [["URL", "url"], ["título", "title"]]
        },
        {
          type: "field_dropdown",
          name: "COMPARISON",
          options: [
            ["contém ignorando maiúsculas", "contains"],
            ["exata", "exact"],
            ["exata ignorando maiúsculas", "caseInsensitive"]
          ]
        }
      ],
      message2: "valor %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["literal", "literal"], ["da configuração", "source"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "parte-da-url" }
      ],
      message3: "elemento inicial opcional %1",
      args3: [{ type: "field_input", name: "READY_SELECTOR", text: "" }],
      previousStatement: null,
      nextStatement: null,
      colour: 205,
      tooltip: "Assume exatamente uma aba já aberta pela URL ou pelo título."
    },
    {
      type: "rpa_close_page",
      message0: "fechar aba atual: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Fechar aba atual" }],
      message1: "elemento inicial opcional da aba anterior %1",
      args1: [{ type: "field_input", name: "READY_SELECTOR", text: "" }],
      previousStatement: null,
      nextStatement: null,
      colour: 205,
      tooltip: "Fecha a aba atual e assume a última aba restante."
    },
    {
      type: "rpa_upload",
      message0: "anexar e aguardar estabilidade: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Anexar arquivo" }],
      message1: "seletor do input file %1",
      args1: [{ type: "field_input", name: "SELECTOR", text: "input[type='file']" }],
      message2: "arquivo %1 opcional %2",
      args2: [
        { type: "field_input", name: "VALUE_SOURCE", text: "attachments.arquivo" },
        { type: "field_checkbox", name: "OPTIONAL", checked: false }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 275,
      tooltip: "Anexa o arquivo e aguarda rede ociosa, loading e formulário estável."
    },
    {
      type: "rpa_wait_stable",
      message0: "aguardar página estável: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Aguardar página estável" }],
      previousStatement: null,
      nextStatement: null,
      colour: 45,
      tooltip: "Aguarda rede ociosa e formulário sem alterações."
    },
    {
      type: "rpa_preserve_fill",
      message0: "preservar ou preencher: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Preservar ou preencher campo" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#campo" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "valor %1 %2 comparação %3",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.valor" },
        {
          type: "field_dropdown",
          name: "COMPARISON",
          options: [
            ["exata", "exact"],
            ["ignorar maiúsculas", "caseInsensitive"],
            ["monetária", "currency"]
          ]
        }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Mantém o valor da página se estiver correto; preenche somente quando vazio."
    },
    {
      type: "rpa_select2",
      message0: "selecionar opção Select2: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Selecionar opção" }],
      message1: "select nativo %1",
      args1: [{ type: "field_input", name: "SELECTOR", text: "#campo" }],
      message2: "controle visível %1",
      args2: [{ type: "field_input", name: "TRIGGER_SELECTOR", text: ".select2-selection" }],
      message3: "opções visíveis %1",
      args3: [{ type: "field_input", name: "OPTION_SELECTOR", text: ".select2-results__option" }],
      message4: "valor %1 %2",
      args4: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.valor" }
      ],
      message5: "comparação %1",
      args5: [{
        type: "field_dropdown",
        name: "COMPARISON",
        options: [
          ["ignorar maiúsculas", "caseInsensitive"],
          ["exata", "exact"],
          ["numérica", "numeric"],
          ["legada do fluxo atual", "legacy"]
        ]
      }],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Rola até o select, abre o Select2 e clica na opção renderizada pela página."
    },
    {
      type: "rpa_currency",
      message0: "preencher campo monetário: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Preencher valor monetário" }],
      message1: "seletor CSS %1",
      args1: [{ type: "field_input", name: "SELECTOR", text: "#campo" }],
      message2: "valor %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["da configuração", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.valor" }
      ],
      message3: "casas decimais %1 intervalo por tecla (ms) %2 confirmar com %3",
      args3: [
        { type: "field_number", name: "DECIMAL_PLACES", value: 2, min: 0, max: 6 },
        { type: "field_number", name: "DELAY", value: 30, min: 0, max: 1000 },
        {
          type: "field_dropdown",
          name: "COMMIT_KEY",
          options: [["Tab", "Tab"], ["Enter", "Enter"]]
        }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Digita os dígitos sequencialmente e deixa a máscara da página formatar o valor."
    },
    {
      type: "rpa_set_variable",
      message0: "definir variável: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Definir valor de execução" }],
      message1: "valor %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [
            ["de um caminho", "source"],
            ["texto literal", "literal"],
            ["JSON literal", "json"]
          ]
        },
        { type: "field_input", name: "VALUE_DATA", text: "input.valor" }
      ],
      message2: "salvar em %1",
      args2: [{ type: "field_input", name: "TARGET", text: "runtime.valor" }],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Copia um valor tipado para runtime.<caminho>."
    },
    {
      type: "rpa_capture_timestamp",
      message0: "capturar instante atual: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Registrar solicitação do código" }],
      message1: "salvar em %1",
      args1: [{
        type: "field_input",
        name: "TARGET",
        text: "runtime.authentication.otpRequestedAt"
      }],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Captura o instante UTC atual e o salva em runtime.<caminho>."
    },
    {
      type: "rpa_wait_one_time_code",
      message0: "aguardar código de autenticação: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Aguardar código de autenticação" }],
      message1: "provider %1",
      args1: [{
        type: "field_input",
        name: "PROVIDER_ALIAS",
        text: "email-otp"
      }],
      message2: "não aceitar código anterior a %1",
      args2: [{
        type: "field_input",
        name: "NOT_BEFORE_SOURCE",
        text: "runtime.authentication.otpRequestedAt"
      }],
      message3: "salvar código em %1",
      args3: [{
        type: "field_input",
        name: "TARGET",
        text: "runtime.authentication.otp"
      }],
      message4: "timeout (ms) %1 intervalo de consulta (ms) %2",
      args4: [
        {
          type: "field_number",
          name: "TIMEOUT_MS",
          value: 120000,
          min: 1000,
          max: 600000
        },
        {
          type: "field_number",
          name: "POLL_INTERVAL_MS",
          value: 5000,
          min: 500,
          max: 60000
        }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 45,
      tooltip: "Consulta um provider configurado até obter um código posterior ao instante informado."
    },
    {
      type: "rpa_transform_path",
      message0: "transformar caminho: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Obter parte do caminho" }],
      message1: "caminho %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["de um caminho de dados", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "attachments.arquivo" }
      ],
      message2: "obter %1",
      args2: [{
        type: "field_dropdown",
        name: "OPERATION",
        options: [
          ["nome do arquivo", "fileName"],
          ["nome sem extensão", "fileNameWithoutExtension"],
          ["extensão", "extension"],
          ["pasta", "directoryName"]
        ]
      }],
      message3: "salvar em %1",
      args3: [{ type: "field_input", name: "TARGET", text: "runtime.nomeArquivo" }],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Obtém uma parte de um caminho local ou UNC sem acessar o sistema de arquivos."
    },
    {
      type: "rpa_read_element",
      message0: "ler elemento: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Capturar valor da página" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#campo" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "propriedade %1 atributo (se usado) %2",
      args2: [
        {
          type: "field_dropdown",
          name: "PROPERTY",
          options: [
            ["valor do campo", "value"],
            ["texto", "text"],
            ["marcado", "checked"],
            ["atributo", "attribute"]
          ]
        },
        { type: "field_input", name: "ATTRIBUTE", text: "" }
      ],
      message3: "salvar em %1",
      args3: [{ type: "field_input", name: "TARGET", text: "runtime.valorCapturado" }],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Lê valor, texto, estado marcado ou atributo e salva no contexto da execução."
    },
    {
      type: "rpa_read_elements",
      message0: "ler vários elementos: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Capturar lista da página" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: ".item" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "propriedade %1 atributo (se usado) %2 máximo %3",
      args2: [
        {
          type: "field_dropdown",
          name: "PROPERTY",
          options: [
            ["valor do campo", "value"],
            ["texto", "text"],
            ["marcado", "checked"],
            ["atributo", "attribute"]
          ]
        },
        { type: "field_input", name: "ATTRIBUTE", text: "" },
        { type: "field_number", name: "MAX_ITEMS", value: 1000, min: 1, max: 10000 }
      ],
      message3: "salvar lista em %1",
      args3: [{ type: "field_input", name: "TARGET", text: "runtime.valoresCapturados" }],
      previousStatement: null,
      nextStatement: null,
      colour: 165,
      tooltip: "Lê zero ou mais elementos em ordem e salva um array em runtime.*."
    },
    {
      type: "rpa_screenshot",
      message0: "salvar screenshot: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Salvar evidência" }],
      message1: "arquivo %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "FILE_MODE",
          options: [["nome literal", "literal"], ["de um caminho", "source"]]
        },
        { type: "field_input", name: "FILE_DATA", text: "evidencia.png" }
      ],
      message2: "pasta %1 %2",
      args2: [
        {
          type: "field_dropdown",
          name: "DIRECTORY_MODE",
          options: [
            ["Runtime.OutputDirectory", "default"],
            ["pasta literal", "literal"],
            ["de um caminho", "source"]
          ]
        },
        { type: "field_input", name: "DIRECTORY_DATA", text: "screenshots" }
      ],
      message3: "separar por execução %1 conflito %2",
      args3: [
        { type: "field_checkbox", name: "SEPARATE_EXECUTION", checked: true },
        {
          type: "field_dropdown",
          name: "CONFLICT",
          options: [
            ["criar nome único", "unique"],
            ["falhar", "fail"],
            ["sobrescrever", "overwrite"]
          ]
        }
      ],
      message4: "salvar caminho final em %1",
      args4: [{ type: "field_input", name: "TARGET", text: "" }],
      previousStatement: null,
      nextStatement: null,
      colour: 120,
      tooltip: "Salva uma captura full-page em um destino configurável."
    },
    {
      type: "rpa_download_click",
      message0: "baixar após clique: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Baixar arquivo da página" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "a.download" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "texto contido %1 timeout (ms) %2",
      args2: [
        { type: "field_input", name: "HAS_TEXT", text: "" },
        { type: "field_number", name: "TIMEOUT", value: 30000, min: 100, max: 600000 }
      ],
      message3: "arquivo %1 %2",
      args3: [
        {
          type: "field_dropdown",
          name: "FILE_MODE",
          options: [
            ["nome sugerido pelo site", "suggested"],
            ["nome literal", "literal"],
            ["de um caminho", "source"]
          ]
        },
        { type: "field_input", name: "FILE_DATA", text: "arquivo.pdf" }
      ],
      message4: "pasta %1 %2",
      args4: [
        {
          type: "field_dropdown",
          name: "DIRECTORY_MODE",
          options: [
            ["Runtime.OutputDirectory", "default"],
            ["pasta literal", "literal"],
            ["de um caminho", "source"]
          ]
        },
        { type: "field_input", name: "DIRECTORY_DATA", text: "downloads" }
      ],
      message5: "separar por execução %1 conflito %2",
      args5: [
        { type: "field_checkbox", name: "SEPARATE_EXECUTION", checked: true },
        {
          type: "field_dropdown",
          name: "CONFLICT",
          options: [
            ["criar nome único", "unique"],
            ["falhar", "fail"],
            ["sobrescrever", "overwrite"]
          ]
        }
      ],
      message6: "salvar caminho final em %1",
      args6: [{ type: "field_input", name: "TARGET", text: "" }],
      previousStatement: null,
      nextStatement: null,
      colour: 120,
      tooltip: "Aguarda o evento de download disparado pelo clique e persiste o arquivo."
    },
    {
      type: "rpa_download_request",
      message0: "baixar por requisição: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Baixar por GET ou POST" }],
      message1: "método %1 URL %2 %3",
      args1: [
        {
          type: "field_dropdown",
          name: "METHOD",
          options: [["GET", "GET"], ["POST", "POST"]]
        },
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["de um caminho", "source"], ["literal", "literal"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "runtime.urlDownload" }
      ],
      message2: "corpo %1 tipo %2 dados %3",
      args2: [
        {
          type: "field_dropdown",
          name: "BODY_MODE",
          options: [
            ["sem corpo", "none"],
            ["de um caminho", "source"],
            ["texto literal", "literal"],
            ["JSON literal", "json"]
          ]
        },
        {
          type: "field_dropdown",
          name: "BODY_TYPE",
          options: [["JSON", "json"], ["texto", "text"], ["formulário", "form"]]
        },
        { type: "field_input", name: "BODY_DATA", text: "input.filtros" }
      ],
      message3: "cabeçalhos %1 %2",
      args3: [
        {
          type: "field_dropdown",
          name: "HEADERS_MODE",
          options: [
            ["sem cabeçalhos extras", "none"],
            ["de um caminho", "source"],
            ["JSON literal", "json"]
          ]
        },
        { type: "field_input", name: "HEADERS_DATA", text: "config.cabecalhosDownload" }
      ],
      message4: "arquivo %1 %2",
      args4: [
        {
          type: "field_dropdown",
          name: "FILE_MODE",
          options: [
            ["nome sugerido pela resposta", "suggested"],
            ["nome literal", "literal"],
            ["de um caminho", "source"]
          ]
        },
        { type: "field_input", name: "FILE_DATA", text: "arquivo.pdf" }
      ],
      message5: "pasta %1 %2",
      args5: [
        {
          type: "field_dropdown",
          name: "DIRECTORY_MODE",
          options: [
            ["Runtime.OutputDirectory", "default"],
            ["pasta literal", "literal"],
            ["de um caminho", "source"]
          ]
        },
        { type: "field_input", name: "DIRECTORY_DATA", text: "downloads" }
      ],
      message6: "separar por execução %1 conflito %2 timeout (ms) %3",
      args6: [
        { type: "field_checkbox", name: "SEPARATE_EXECUTION", checked: true },
        {
          type: "field_dropdown",
          name: "CONFLICT",
          options: [
            ["criar nome único", "unique"],
            ["falhar", "fail"],
            ["sobrescrever", "overwrite"]
          ]
        },
        { type: "field_number", name: "TIMEOUT", value: 30000, min: 100, max: 600000 }
      ],
      message7: "salvar caminho final em %1",
      args7: [{ type: "field_input", name: "TARGET", text: "" }],
      previousStatement: null,
      nextStatement: null,
      colour: 120,
      tooltip: "Faz GET ou POST autenticado com os cookies do contexto e salva o corpo da resposta."
    },
    {
      type: "rpa_safe_final",
      message0: "processar confirmação final protegida: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Processar confirmação final" }],
      message1: "seletor do botão %1",
      args1: [{ type: "field_input", name: "SELECTOR", text: "button[type='submit']" }],
      message2: "comprovar conclusão e publicar feedback %1",
      args2: [{ type: "field_checkbox", name: "VALIDATE_COMPLETION", checked: true }],
      message3: "quando marcado, comprovar sucesso em %1 contendo %2",
      args3: [
        { type: "field_input", name: "SUCCESS_SELECTOR", text: "p.mensagem-sucesso" },
        { type: "field_input", name: "SUCCESS_TEXT", text: "Operação concluída" }
      ],
      message4: "extrair protocolo de %1 com expressão %2",
      args4: [
        { type: "field_input", name: "PROTOCOL_SELECTOR", text: "body" },
        {
          type: "field_input",
          name: "PROTOCOL_PATTERN",
          text: "#(?<protocol>\\d+)"
        }
      ],
      message5: "destinos: conclusão %1 mensagem %2 protocolo %3",
      args5: [
        {
          type: "field_input",
          name: "COMPLETION_TARGET",
          text: "runtime.business.completed"
        },
        {
          type: "field_input",
          name: "CONFIRMATION_MESSAGE_TARGET",
          text: "runtime.business.confirmationMessage"
        },
        {
          type: "field_input",
          name: "PROTOCOL_TARGET",
          text: "runtime.business.protocol"
        }
      ],
      message6: "timeout da confirmação (ms) %1",
      args6: [
        { type: "field_number", name: "TIMEOUT", value: 60000, min: 100, max: 600000 }
      ],
      message7: "screenshot antes da confirmação %1 %2",
      args7: [
        {
          type: "field_dropdown",
          name: "FILE_MODE",
          options: [["nome literal", "literal"], ["de um caminho", "source"]]
        },
        { type: "field_input", name: "FILE_DATA", text: "antes-da-confirmacao.png" }
      ],
      message8: "pasta %1 %2 separar por execução %3",
      args8: [
        {
          type: "field_dropdown",
          name: "DIRECTORY_MODE",
          options: [
            ["Runtime.OutputDirectory", "default"],
            ["pasta literal", "literal"],
            ["de um caminho", "source"]
          ]
        },
        { type: "field_input", name: "DIRECTORY_DATA", text: "screenshots" },
        { type: "field_checkbox", name: "SEPARATE_EXECUTION", checked: true }
      ],
      message9: "conflito %1",
      args9: [{
        type: "field_dropdown",
        name: "CONFLICT",
        options: [
          ["criar nome único", "unique"],
          ["falhar", "fail"],
          ["sobrescrever", "overwrite"]
        ]
      }],
      message10: "salvar caminho do screenshot em %1",
      args10: [{ type: "field_input", name: "TARGET", text: "" }],
      previousStatement: null,
      colour: 5,
      tooltip: "A caixa controla somente a comprovação e a publicação do feedback. O host seguro sempre cancela o alerta; somente um host autorizado externamente pode confirmar o envio."
    },
    {
      type: "rpa_fail",
      message0: "interromper com erro: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Interromper o fluxo" }],
      message1: "mensagem %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "VALUE_MODE",
          options: [["literal", "literal"], ["de um caminho", "source"]]
        },
        { type: "field_input", name: "VALUE_DATA", text: "Motivo da interrupção" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 5,
      tooltip: "Interrompe imediatamente a execução com uma mensagem clara, sem efeito externo."
    },
    {
      type: "rpa_complete_authentication_attempt",
      message0: "concluir tentativa de autenticação: %1",
      args0: [{
        type: "field_input",
        name: "NAME",
        text: "Concluir tentativa de autenticação"
      }],
      previousStatement: null,
      nextStatement: null,
      colour: 315,
      tooltip: "Libera a cerca de retry somente quando o fluxo comprova que a tentativa de autenticação foi concluída."
    },
    {
      type: "rpa_if_value",
      message0: "se valor: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Verificar valor" }],
      message1: "esquerda %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "LEFT_MODE",
          options: [
            ["da configuração/loop", "source"],
            ["texto literal", "literal"],
            ["JSON literal", "json"]
          ]
        },
        { type: "field_input", name: "LEFT_DATA", text: "config.condicao" }
      ],
      message2: "operador %1",
      args2: [{
        type: "field_dropdown",
        name: "OPERATOR",
        options: [
          ["igual", "equals"],
          ["diferente", "notEquals"],
          ["contém", "contains"],
          ["não contém", "notContains"],
          ["começa com", "startsWith"],
          ["termina com", "endsWith"],
          ["expressão regular", "matchesRegex"],
          ["está vazio", "isEmpty"],
          ["não está vazio", "isNotEmpty"]
        ]
      }],
      message3: "direita %1 %2 ignorar maiúsculas %3",
      args3: [
        {
          type: "field_dropdown",
          name: "RIGHT_MODE",
          options: [
            ["texto literal", "literal"],
            ["JSON literal", "json"],
            ["da configuração/loop", "source"]
          ]
        },
        { type: "field_input", name: "RIGHT_DATA", text: "sim" },
        { type: "field_checkbox", name: "IGNORE_CASE", checked: false }
      ],
      message4: "então %1",
      args4: [{ type: "input_statement", name: "THEN" }],
      message5: "senão %1",
      args5: [{ type: "input_statement", name: "ELSE" }],
      previousStatement: null,
      nextStatement: null,
      colour: 315,
      tooltip: "Executa somente o ramo correspondente à comparação de valores."
    },
    {
      type: "rpa_if_element",
      message0: "se elemento: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Verificar elemento" }],
      message1: "seletor CSS %1 escopo %2",
      args1: [
        { type: "field_input", name: "SELECTOR", text: "#elemento" },
        { type: "field_input", name: "SCOPE", text: "" }
      ],
      message2: "texto contido opcional %1",
      args2: [{ type: "field_input", name: "HAS_TEXT", text: "" }],
      message3: "estado atual %1",
      args3: [{
        type: "field_dropdown",
        name: "STATE",
        options: [
          ["visível", "visible"],
          ["anexado ao DOM", "attached"],
          ["oculto", "hidden"],
          ["fora do DOM", "detached"]
        ]
      }],
      message4: "correspondência do seletor %1",
      args4: [{
        type: "field_dropdown",
        name: "MATCH_MODE",
        options: [
          ["exatamente um elemento", "single"],
          ["primeiro elemento (compatibilidade)", "first"]
        ]
      }],
      message5: "então %1",
      args5: [{ type: "input_statement", name: "THEN" }],
      message6: "senão %1",
      args6: [{ type: "input_statement", name: "ELSE" }],
      previousStatement: null,
      nextStatement: null,
      colour: 315,
      tooltip: "Testa imediatamente o estado atual do elemento, sem espera fixa."
    },
    {
      type: "rpa_repeat",
      message0: "repetir: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Repetir ações" }],
      message1: "quantidade %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "COUNT_MODE",
          options: [["literal", "literal"], ["da configuração", "source"]]
        },
        { type: "field_input", name: "COUNT_DATA", text: "2" }
      ],
      message2: "nome temporário do índice %1",
      args2: [{ type: "field_input", name: "INDEX_VARIABLE", text: "repeatIndex" }],
      message3: "ações %1",
      args3: [{ type: "input_statement", name: "DO" }],
      previousStatement: null,
      nextStatement: null,
      colour: 255,
      tooltip: "Repete o conjunto interno e expõe o índice atual em loop.<nome>."
    },
    {
      type: "rpa_for_each",
      message0: "para cada item: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Percorrer lista" }],
      message1: "lista %1 %2",
      args1: [
        {
          type: "field_dropdown",
          name: "ITEMS_MODE",
          options: [["de um caminho", "source"], ["JSON literal", "literal"]]
        },
        { type: "field_input", name: "ITEMS_DATA", text: "config.minhaLista" }
      ],
      message2: "nome temporário do item %1",
      args2: [{ type: "field_input", name: "ITEM_VARIABLE", text: "item" }],
      message3: "nome temporário do índice %1",
      args3: [{ type: "field_input", name: "INDEX_VARIABLE", text: "itemIndex" }],
      message4: "ações %1",
      args4: [{ type: "input_statement", name: "DO" }],
      previousStatement: null,
      nextStatement: null,
      colour: 255,
      tooltip: "Percorre arrays de valores, objetos ou outras listas e cria um escopo loop por nível."
    },
    {
      type: "rpa_run_subflow",
      message0: "executar subfluxo: %1",
      args0: [{ type: "field_input", name: "NAME", text: "Executar subfluxo" }],
      message1: "nome do subfluxo %1",
      args1: [{ type: "field_input", name: "SUBFLOW", text: "meuSubfluxo" }],
      previousStatement: null,
      nextStatement: null,
      colour: 195,
      tooltip: "Executa uma sequência reutilizável definida em outro bloco raiz."
    },
    {
      type: "rpa_subflow_definition",
      message0: "definir subfluxo %1",
      args0: [{ type: "field_input", name: "SUBFLOW", text: "meuSubfluxo" }],
      message1: "ações %1",
      args1: [{ type: "input_statement", name: "ACTIONS" }],
      colour: 195,
      tooltip: "Define uma sequência reutilizável. Mantenha este bloco separado da sequência principal."
    }
  ];

  const locatorBlockTypes = new Set([
    "rpa_click",
    "rpa_click_optional",
    "rpa_wait",
    "rpa_fill",
    "rpa_select_option",
    "rpa_set_checked",
    "rpa_press_key",
    "rpa_type_sequentially",
    "rpa_type_across_inputs",
    "rpa_click_new_page",
    "rpa_upload",
    "rpa_preserve_fill",
    "rpa_select2",
    "rpa_currency",
    "rpa_read_element",
    "rpa_read_elements",
    "rpa_download_click",
    "rpa_safe_final",
    "rpa_if_element"
  ]);

  function definedFieldNames(block) {
    return new Set(
      Object.keys(block)
        .filter(key => /^args\d+$/.test(key))
        .flatMap(key => block[key])
        .map(argument => argument.name)
        .filter(Boolean));
  }

  function appendBlockFields(block, message, args) {
    const messageIndexes = Object.keys(block)
      .filter(key => /^message\d+$/.test(key))
      .map(key => Number(key.slice("message".length)));
    const nextIndex = Math.max(...messageIndexes) + 1;
    block[`message${nextIndex}`] = message;
    block[`args${nextIndex}`] = args;
  }

  for (const block of blocks) {
    if (!locatorBlockTypes.has(block.type)) continue;
    const fieldNames = definedFieldNames(block);
    if (!fieldNames.has("SCOPE")) {
      appendBlockFields(block, "escopo CSS opcional %1", [{
        type: "field_input",
        name: "SCOPE",
        text: ""
      }]);
    }
    appendBlockFields(block, "texto do escopo literal %1 origem opcional %2", [
      { type: "field_input", name: "SCOPE_HAS_TEXT", text: "" },
      { type: "field_input", name: "SCOPE_HAS_TEXT_SOURCE", text: "" }
    ]);
    if (!fieldNames.has("HAS_TEXT")) {
      appendBlockFields(block, "texto do alvo literal %1 origem opcional %2", [
        { type: "field_input", name: "HAS_TEXT", text: "" },
        { type: "field_input", name: "HAS_TEXT_SOURCE", text: "" }
      ]);
    } else {
      appendBlockFields(block, "origem opcional do texto do alvo %1", [{
        type: "field_input",
        name: "HAS_TEXT_SOURCE",
        text: ""
      }]);
    }
    appendBlockFields(block, "iframes externos → internos (JSON) %1", [{
      type: "field_input",
      name: "FRAME_SELECTORS",
      text: "[]"
    }]);
  }

  Blockly.common.defineBlocksWithJsonArray(blocks);

  const toolboxDefinition = {
    kind: "categoryToolbox",
    contents: [
      {
        kind: "category",
        name: "Navegação e cliques",
        colour: 205,
        contents: [
          { kind: "block", type: "rpa_navigate" },
          { kind: "block", type: "rpa_click" },
          { kind: "block", type: "rpa_click_optional" },
          { kind: "block", type: "rpa_click_new_page" },
          { kind: "block", type: "rpa_switch_page" },
          { kind: "block", type: "rpa_close_page" }
        ]
      },
      {
        kind: "category",
        name: "Esperas",
        colour: 45,
        contents: [
          { kind: "block", type: "rpa_wait" },
          { kind: "block", type: "rpa_wait_stable" },
          { kind: "block", type: "rpa_wait_one_time_code" }
        ]
      },
      {
        kind: "category",
        name: "Formulários",
        colour: 165,
        contents: [
          { kind: "block", type: "rpa_fill" },
          { kind: "block", type: "rpa_select_option" },
          { kind: "block", type: "rpa_set_checked" },
          { kind: "block", type: "rpa_press_key" },
          { kind: "block", type: "rpa_type_sequentially" },
          { kind: "block", type: "rpa_type_across_inputs" },
          { kind: "block", type: "rpa_upload" },
          { kind: "block", type: "rpa_preserve_fill" },
          { kind: "block", type: "rpa_select2" },
          { kind: "block", type: "rpa_currency" },
          { kind: "block", type: "rpa_set_variable" },
          { kind: "block", type: "rpa_capture_timestamp" },
          { kind: "block", type: "rpa_transform_path" },
          { kind: "block", type: "rpa_read_element" },
          { kind: "block", type: "rpa_read_elements" }
        ]
      },
      {
        kind: "category",
        name: "Condições e repetições",
        colour: 315,
        contents: [
          { kind: "block", type: "rpa_if_value" },
          { kind: "block", type: "rpa_if_element" },
          { kind: "block", type: "rpa_fail" },
          { kind: "block", type: "rpa_complete_authentication_attempt" },
          { kind: "block", type: "rpa_repeat" },
          { kind: "block", type: "rpa_for_each" }
        ]
      },
      {
        kind: "category",
        name: "Subfluxos",
        colour: 195,
        contents: [
          { kind: "block", type: "rpa_run_subflow" },
          { kind: "block", type: "rpa_subflow_definition" }
        ]
      },
      {
        kind: "category",
        name: "Arquivos, evidência e segurança",
        colour: 5,
        contents: [
          { kind: "block", type: "rpa_screenshot" },
          { kind: "block", type: "rpa_download_click" },
          { kind: "block", type: "rpa_download_request" },
          { kind: "block", type: "rpa_safe_final" }
        ]
      }
    ]
  };

  function createToolbox() {
    return structuredClone(toolboxDefinition);
  }

  let defaultFlow = {
    schemaVersion: 1,
    name: "Novo fluxo de RPA",
    inputs: [],
    subflows: {},
    actions: [
      {
        id: "iniciar-fluxo",
        type: "setVariable",
        name: "Iniciar fluxo",
        value: "pronto",
        target: "runtime.estado"
      }
    ]
  };

  let configurationFieldDefinitions = [];

  let serverSession = null;
  let editorProfile = null;
  let loadedConfiguration = null;
  let loadedFlowName = defaultFlow.name;
  let loadedFlowInputs = structuredClone(defaultFlow.inputs);

  const actionToBlockType = {
    navigate: "rpa_navigate",
    click: "rpa_click",
    clickIfVisible: "rpa_click_optional",
    wait: "rpa_wait",
    fill: "rpa_fill",
    selectOption: "rpa_select_option",
    setChecked: "rpa_set_checked",
    pressKey: "rpa_press_key",
    typeSequentially: "rpa_type_sequentially",
    typeAcrossInputs: "rpa_type_across_inputs",
    clickAndSwitchPage: "rpa_click_new_page",
    upload: "rpa_upload",
    waitStable: "rpa_wait_stable",
    preserveOrFill: "rpa_preserve_fill",
    select2: "rpa_select2",
    fillMaskedCurrency: "rpa_currency",
    fail: "rpa_fail",
    completeAuthenticationAttempt: "rpa_complete_authentication_attempt",
    transformPath: "rpa_transform_path",
    setVariable: "rpa_set_variable",
    captureTimestamp: "rpa_capture_timestamp",
    waitForOneTimeCode: "rpa_wait_one_time_code",
    readElement: "rpa_read_element",
    readElements: "rpa_read_elements",
    switchPage: "rpa_switch_page",
    closePage: "rpa_close_page",
    screenshot: "rpa_screenshot",
    safeFinalConfirmation: "rpa_safe_final",
    repeat: "rpa_repeat",
    forEach: "rpa_for_each",
    runSubflow: "rpa_run_subflow"
  };

  const workspace = Blockly.inject("blockly-editor", {
    toolbox: createToolbox(),
    media: "vendor/blockly/media/",
    renderer: "zelos",
    trashcan: true,
    move: { scrollbars: true, drag: true, wheel: true },
    zoom: { controls: true, wheel: true, startScale: 0.72, maxScale: 1.4, minScale: 0.35 },
    grid: { spacing: 20, length: 3, colour: "#d9e2ec", snap: true }
  });

  const generatedJson = document.getElementById("generated-json");
  const validationMessage = document.getElementById("validation-message");
  const workspaceFile = document.getElementById("workspace-file");
  const flowFile = document.getElementById("flow-file");
  const serverStatus = document.getElementById("server-status");
  const configurationDialog = document.getElementById("configuration-dialog");
  const configurationFields = document.getElementById("configuration-fields");
  const variablesList = document.getElementById("variables-list");
  const configurationMessage = document.getElementById("configuration-message");
  const configurationFileLabel = document.getElementById("configuration-file-label");
  const newVariableKey = document.getElementById("new-variable-key");
  const newVariableType = document.getElementById("new-variable-type");
  const newVariableValue = document.getElementById("new-variable-value");

  function nonEmpty(value) {
    const text = String(value ?? "").trim();
    return text || undefined;
  }

  function hasOwn(value, property) {
    return Object.prototype.hasOwnProperty.call(value, property);
  }

  const dataPathPattern =
    /^(input|job|config|variables|attachments|runtime|system|loop)\.[A-Za-z][A-Za-z0-9_-]*(\[[0-9]+\])?(\.[A-Za-z][A-Za-z0-9_-]*(\[[0-9]+\])?)*$/i;
  const runtimeTargetPattern =
    /^runtime\.[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z][A-Za-z0-9_-]*)*$/i;
  const providerAliasPattern = /^[A-Za-z][A-Za-z0-9._-]*$/;

  function invalidLocatorSource(locator) {
    return [locator?.scopeHasTextSource, locator?.hasTextSource]
      .find(source => source && !dataPathPattern.test(source));
  }

  function typeAcrossInputsValidationError(action) {
    if (action?.type !== "typeAcrossInputs") return null;

    const actionName = action.name || action.id || "typeAcrossInputs";
    if (!nonEmpty(action.selector)) {
      return `Preencha o seletor da ação '${actionName}'.`;
    }

    const hasLiteral = hasOwn(action, "value");
    const hasSource = Boolean(nonEmpty(action.valueSource));
    if (hasLiteral === hasSource) {
      return `Informe exatamente value ou valueSource em '${actionName}'.`;
    }
    if (hasSource && !dataPathPattern.test(action.valueSource)) {
      return `A origem do valor de '${actionName}' deve ser um caminho de dados válido.`;
    }
    if (hasOwn(action, "matchMode")) {
      return `A ação '${actionName}' não aceita matchMode.`;
    }
    if (action.delayMs !== undefined &&
        (!Number.isInteger(action.delayMs) ||
         action.delayMs < 0 || action.delayMs > 1000)) {
      return `O intervalo de '${actionName}' deve ser um inteiro entre 0 e 1000 ms.`;
    }
    if (action.clearFirst !== undefined && typeof action.clearFirst !== "boolean") {
      return `clearFirst de '${actionName}' deve ser booleano.`;
    }
    if (action.blurAfter !== undefined && typeof action.blurAfter !== "boolean") {
      return `blurAfter de '${actionName}' deve ser booleano.`;
    }

    return null;
  }

  function safeFinalConfirmationValidationError(action) {
    if (action?.type !== "safeFinalConfirmation") return null;

    const actionName = action.name || action.id || "safeFinalConfirmation";
    const fields = [
      ["successSelector", action.successSelector],
      ["successText", action.successText],
      ["protocolSelector", action.protocolSelector],
      ["protocolPattern", action.protocolPattern],
      ["completionTarget", action.completionTarget],
      ["confirmationMessageTarget", action.confirmationMessageTarget],
      ["protocolTarget", action.protocolTarget]
    ];
    const configured = fields.filter(([, value]) => nonEmpty(value));
    if (!configured.length) return null;

    const missing = fields
      .filter(([, value]) => !nonEmpty(value))
      .map(([name]) => name);
    if (missing.length) {
      return `A confirmação '${actionName}' possui configuração de sucesso incompleta: ${missing.join(", ")}.`;
    }

    const targets = [
      action.completionTarget,
      action.confirmationMessageTarget,
      action.protocolTarget
    ];
    if (targets.some(target => !runtimeTargetPattern.test(target))) {
      return `Os destinos de conclusão de '${actionName}' devem usar runtime.<caminho>.`;
    }
    if (new Set(targets.map(target => target.toLowerCase())).size !== targets.length) {
      return `Os destinos de conclusão de '${actionName}' devem ser diferentes.`;
    }
    if (!/\(\?<protocol>/.test(action.protocolPattern)) {
      return `A expressão de protocolo de '${actionName}' deve possuir o grupo nomeado 'protocol'.`;
    }
    try {
      new RegExp(action.protocolPattern);
    } catch {
      return `A expressão de protocolo de '${actionName}' é inválida.`;
    }
    if (action.timeoutMs !== undefined &&
        (!Number.isInteger(action.timeoutMs) ||
         action.timeoutMs < 100 || action.timeoutMs > 600000)) {
      return `O timeout de '${actionName}' deve ser um inteiro entre 100 e 600000 ms.`;
    }

    return null;
  }

  function setField(block, name, value) {
    if (value !== undefined && value !== null && block.getField(name)) {
      block.setFieldValue(String(value), name);
    }
  }

  function setValueFields(block, action) {
    if (action.valueSource) {
      setField(block, "VALUE_MODE", "source");
      setField(block, "VALUE_DATA", action.valueSource);
      setField(block, "VALUE_SOURCE", action.valueSource);
    } else if (action.value !== undefined) {
      const jsonLiteral = ["setVariable", "setChecked"].includes(action.type) &&
        typeof action.value !== "string";
      setField(block, "VALUE_MODE", jsonLiteral ? "json" : "literal");
      setField(block, "VALUE_DATA", jsonLiteral ? JSON.stringify(action.value) : action.value);
    }
  }

  function setConditionValueFields(block, condition, side) {
    const source = condition[`${side}Source`];
    const value = condition[`${side}Value`];
    const fieldPrefix = side.toUpperCase();
    if (source) {
      setField(block, `${fieldPrefix}_MODE`, "source");
      setField(block, `${fieldPrefix}_DATA`, source);
    } else if (value !== undefined) {
      const jsonLiteral = typeof value !== "string";
      setField(block, `${fieldPrefix}_MODE`, jsonLiteral ? "json" : "literal");
      setField(
        block,
        `${fieldPrefix}_DATA`,
        jsonLiteral ? JSON.stringify(value) : value);
    }
  }

  function setArtifactDestinationFields(block, action) {
    const directoryMode = action.destinationDirectorySource
      ? "source"
      : action.destinationDirectory !== undefined
        ? "literal"
        : "default";
    setField(block, "DIRECTORY_MODE", directoryMode);
    setField(
      block,
      "DIRECTORY_DATA",
      action.destinationDirectorySource ?? action.destinationDirectory);

    const legacyScreenshotName = action.screenshotName;
    const fileMode = action.fileNameSource
      ? "source"
      : action.fileName !== undefined || legacyScreenshotName !== undefined
        ? "literal"
        : "suggested";
    setField(block, "FILE_MODE", fileMode);
    setField(
      block,
      "FILE_DATA",
      action.fileNameSource ?? action.fileName ?? legacyScreenshotName);
    setField(
      block,
      "SEPARATE_EXECUTION",
      action.separateByExecution === false ? "FALSE" : "TRUE");
    setField(block, "CONFLICT", action.conflictStrategy ?? "unique");
  }

  function setRequestFields(block, action) {
    setField(block, "METHOD", action.method ?? "GET");
    setField(block, "BODY_TYPE", action.bodyType ?? "json");

    if (action.requestBodySource) {
      setField(block, "BODY_MODE", "source");
      setField(block, "BODY_DATA", action.requestBodySource);
    } else if (action.requestBody !== undefined) {
      const bodyIsText = typeof action.requestBody === "string";
      setField(block, "BODY_MODE", bodyIsText ? "literal" : "json");
      setField(
        block,
        "BODY_DATA",
        bodyIsText ? action.requestBody : JSON.stringify(action.requestBody));
    } else {
      setField(block, "BODY_MODE", "none");
    }

    if (action.requestHeadersSource) {
      setField(block, "HEADERS_MODE", "source");
      setField(block, "HEADERS_DATA", action.requestHeadersSource);
    } else if (action.requestHeaders !== undefined) {
      setField(block, "HEADERS_MODE", "json");
      setField(block, "HEADERS_DATA", JSON.stringify(action.requestHeaders));
    } else {
      setField(block, "HEADERS_MODE", "none");
    }
  }

  function createBlock(action) {
    let blockType;
    if (action.type === "if") {
      if (action.condition?.type === "element") {
        blockType = "rpa_if_element";
      } else if (action.condition?.type === "value") {
        blockType = "rpa_if_value";
      } else {
        throw new Error(
          `Tipo de condição não suportado pelo editor: ${action.condition?.type ?? "ausente"}`);
      }
    } else if (action.type === "download") {
      blockType = action.downloadMode === "request"
        ? "rpa_download_request"
        : "rpa_download_click";
    } else {
      blockType = actionToBlockType[action.type];
    }
    if (!blockType) {
      throw new Error(`Tipo de ação não suportado pelo editor: ${action.type}`);
    }

    const block = workspace.newBlock(blockType);
    block.data = JSON.stringify({ actionId: action.id });
    setField(block, "NAME", action.name);
    setField(block, "SELECTOR", action.selector);
    setField(block, "SCOPE", action.scope);
    setField(block, "SCOPE_HAS_TEXT", action.scopeHasText);
    setField(block, "SCOPE_HAS_TEXT_SOURCE", action.scopeHasTextSource);
    setField(block, "FRAME_SELECTORS", JSON.stringify(action.frameSelectors ?? []));
    setField(block, "HAS_TEXT", action.hasText);
    setField(block, "HAS_TEXT_SOURCE", action.hasTextSource);
    setField(block, "STATE", action.state);
    setField(
      block,
      "COMPARISON",
      action.type === "select2" ? action.comparison ?? "legacy" : action.comparison);
    if (action.type === "wait") {
      setField(block, "MATCH_MODE", action.matchMode ?? "first");
    }
    setField(block, "OPERATION", action.operation);
    setField(block, "OPTION_MODE", action.optionMode);
    setField(block, "TRIGGER_SELECTOR", action.triggerSelector);
    setField(block, "OPTION_SELECTOR", action.optionSelector);
    setField(block, "READY_SELECTOR", action.readySelector);
    if (action.type === "safeFinalConfirmation") {
      const completionFields = [
        action.successSelector,
        action.successText,
        action.protocolSelector,
        action.protocolPattern,
        action.completionTarget,
        action.confirmationMessageTarget,
        action.protocolTarget
      ];
      setField(
        block,
        "VALIDATE_COMPLETION",
        completionFields.some(nonEmpty) ? "TRUE" : "FALSE");
    }
    setField(block, "SUCCESS_SELECTOR", action.successSelector);
    setField(block, "SUCCESS_TEXT", action.successText);
    setField(block, "PROTOCOL_SELECTOR", action.protocolSelector);
    setField(block, "PROTOCOL_PATTERN", action.protocolPattern);
    setField(block, "COMPLETION_TARGET", action.completionTarget);
    setField(
      block,
      "CONFIRMATION_MESSAGE_TARGET",
      action.confirmationMessageTarget);
    setField(block, "PROTOCOL_TARGET", action.protocolTarget);
    setField(block, "SCREENSHOT", action.screenshotName);
    setField(
      block,
      "TIMEOUT",
      action.timeoutMs ?? (action.type === "safeFinalConfirmation" ? 60000 : 0));
    setField(
      block,
      "DELAY",
      action.delayMs ?? (action.type === "fillMaskedCurrency" ? 30 : 50));
    setField(block, "DECIMAL_PLACES", action.decimalPlaces ?? 2);
    setField(block, "COMMIT_KEY", action.commitKey ?? "Tab");
    setField(block, "CLEAR_FIRST", action.clearFirst ? "TRUE" : "FALSE");
    setField(block, "BLUR_AFTER", action.blurAfter ? "TRUE" : "FALSE");
    setField(block, "OPTIONAL", action.optional ? "TRUE" : "FALSE");
    setField(block, "TARGET", action.target);
    setField(block, "PROVIDER_ALIAS", action.providerAlias);
    setField(block, "NOT_BEFORE_SOURCE", action.notBeforeSource);
    setField(block, "TIMEOUT_MS", action.timeoutMs ?? 120000);
    setField(block, "POLL_INTERVAL_MS", action.pollIntervalMs ?? 5000);
    setField(block, "PROPERTY", action.property);
    setField(block, "ATTRIBUTE", action.attribute);
    setField(block, "MAX_ITEMS", action.maxItems ?? 1000);
    setValueFields(block, action);
    if (["screenshot", "safeFinalConfirmation", "download"].includes(action.type)) {
      setArtifactDestinationFields(block, action);
    }
    if (action.type === "download") {
      setField(block, "DOWNLOAD_MODE", action.downloadMode ?? "click");
      if (action.downloadMode === "request") {
        setRequestFields(block, action);
      }
    }

    if (action.type === "if" && action.condition?.type === "value") {
      const condition = action.condition;
      setField(block, "OPERATOR", condition.operator);
      setField(block, "IGNORE_CASE", condition.ignoreCase ? "TRUE" : "FALSE");
      setConditionValueFields(block, condition, "left");
      setConditionValueFields(block, condition, "right");
    } else if (action.type === "if" && action.condition?.type === "element") {
      setField(block, "SELECTOR", action.condition.selector);
      setField(block, "SCOPE", action.condition.scope);
      setField(block, "SCOPE_HAS_TEXT", action.condition.scopeHasText);
      setField(
        block,
        "SCOPE_HAS_TEXT_SOURCE",
        action.condition.scopeHasTextSource);
      setField(
        block,
        "FRAME_SELECTORS",
        JSON.stringify(action.condition.frameSelectors ?? []));
      setField(block, "HAS_TEXT", action.condition.hasText);
      setField(block, "HAS_TEXT_SOURCE", action.condition.hasTextSource);
      setField(block, "STATE", action.condition.state);
      setField(block, "MATCH_MODE", action.condition.matchMode ?? "first");
    } else if (action.type === "repeat") {
      setField(block, "COUNT_MODE", action.timesSource ? "source" : "literal");
      setField(block, "COUNT_DATA", action.timesSource ?? action.times ?? 0);
      setField(block, "INDEX_VARIABLE", action.indexVariable ?? "repeatIndex");
    } else if (action.type === "forEach") {
      setField(block, "ITEMS_MODE", action.itemsSource ? "source" : "literal");
      setField(block, "ITEMS_DATA", action.itemsSource ?? JSON.stringify(action.items ?? []));
      setField(block, "ITEM_VARIABLE", action.itemVariable);
      setField(block, "INDEX_VARIABLE", action.indexVariable ?? `${action.itemVariable}Index`);
    } else if (action.type === "runSubflow") {
      setField(block, "SUBFLOW", action.subflow);
    }

    block.initSvg();
    block.render();

    if (action.type === "if") {
      createActionChain(action.actions ?? [], block.getInput("THEN").connection);
      createActionChain(action.elseActions ?? [], block.getInput("ELSE").connection);
    } else if (action.type === "repeat" || action.type === "forEach") {
      createActionChain(action.actions ?? [], block.getInput("DO").connection);
    }

    return block;
  }

  function createActionChain(actions, parentConnection = null) {
    let previous = null;
    let first = null;
    for (const action of actions) {
      const block = createBlock(action);
      first ??= block;
      if (previous) {
        previous.nextConnection.connect(block.previousConnection);
      } else if (parentConnection) {
        parentConnection.connect(block.previousConnection);
      }
      previous = block;
    }
    return first;
  }

  function loadProductionFlow(flow) {
    if (flow?.schemaVersion !== 1 ||
        !Array.isArray(flow.actions) || !flow.actions.length) {
      throw new Error("O JSON deve possuir schemaVersion 1 e uma lista actions não vazia.");
    }

    const importedActions = [
      ...allNestedActions(flow.actions),
      ...Object.values(flow.subflows || {})
        .filter(Array.isArray)
        .flatMap(allNestedActions)
    ];
    const invalidTypeAcrossInputs = importedActions
      .map(typeAcrossInputsValidationError)
      .find(Boolean);
    if (invalidTypeAcrossInputs) {
      throw new Error(invalidTypeAcrossInputs);
    }
    const invalidSafeFinalConfirmation = importedActions
      .map(safeFinalConfirmationValidationError)
      .find(Boolean);
    if (invalidSafeFinalConfirmation) {
      throw new Error(invalidSafeFinalConfirmation);
    }

    workspace.clear();
    loadedFlowName = nonEmpty(flow.name) || defaultFlow.name;
    loadedFlowInputs = Array.isArray(flow.inputs) ? structuredClone(flow.inputs) : [];
    const mainBlock = createActionChain(flow.actions);
    mainBlock?.moveBy(70, 45);

    let subflowIndex = 0;
    for (const [name, actions] of Object.entries(flow.subflows || {})) {
      const definition = workspace.newBlock("rpa_subflow_definition");
      setField(definition, "SUBFLOW", name);
      definition.initSvg();
      definition.render();
      createActionChain(actions, definition.getInput("ACTIONS").connection);
      definition.moveBy(720, 45 + (subflowIndex * 180));
      subflowIndex += 1;
    }
    refreshJson();
  }

  function actionId(block, index) {
    try {
      const parsed = JSON.parse(block.data || "{}");
      if (parsed.actionId) {
        return parsed.actionId;
      }
    } catch {
      // Um ID determinístico será criado abaixo.
    }

    const id = `${block.type.replace(/^rpa_/, "").replaceAll("_", "-")}-${index + 1}`;
    block.data = JSON.stringify({ actionId: id });
    return id;
  }

  function addLocator(action, block, description = action.name) {
    action.selector = nonEmpty(block.getFieldValue("SELECTOR"));
    const scope = nonEmpty(block.getFieldValue("SCOPE"));
    const scopeHasText = nonEmpty(block.getFieldValue("SCOPE_HAS_TEXT"));
    const scopeHasTextSource = nonEmpty(
      block.getFieldValue("SCOPE_HAS_TEXT_SOURCE"));
    const hasText = nonEmpty(block.getFieldValue("HAS_TEXT"));
    const hasTextSource = nonEmpty(block.getFieldValue("HAS_TEXT_SOURCE"));
    if (scopeHasText && scopeHasTextSource) {
      throw new Error(
        `Use somente texto literal ou origem para o escopo de '${description}'.`);
    }
    if (hasText && hasTextSource) {
      throw new Error(
        `Use somente texto literal ou origem para o alvo de '${description}'.`);
    }
    if ((scopeHasText || scopeHasTextSource) && !scope) {
      throw new Error(
        `Informe o escopo CSS antes do texto de escopo de '${description}'.`);
    }
    const frameSelectors = parseFrameSelectors(
      block.getFieldValue("FRAME_SELECTORS"),
      description);
    if (scope) action.scope = scope;
    if (scopeHasText) action.scopeHasText = scopeHasText;
    if (scopeHasTextSource) action.scopeHasTextSource = scopeHasTextSource;
    if (hasText) action.hasText = hasText;
    if (hasTextSource) action.hasTextSource = hasTextSource;
    if (frameSelectors.length) action.frameSelectors = frameSelectors;
  }

  function parseFrameSelectors(rawValue, actionName) {
    const text = String(rawValue ?? "").trim() || "[]";
    let selectors;
    try {
      selectors = JSON.parse(text);
    } catch {
      throw new Error(`Os iframes de '${actionName}' devem formar uma lista JSON válida.`);
    }

    if (!Array.isArray(selectors) || selectors.length > 8 ||
        selectors.some(selector => typeof selector !== "string" || !selector.trim())) {
      throw new Error(
        `Os iframes de '${actionName}' devem ser uma lista de até 8 seletores CSS não vazios.`);
    }

    return selectors.map(selector => selector.trim());
  }

  function addValue(action, block) {
    const sourceField = block.getField("VALUE_SOURCE");
    if (sourceField) {
      action.valueSource = nonEmpty(block.getFieldValue("VALUE_SOURCE"));
      return;
    }

    const value = String(block.getFieldValue("VALUE_DATA") ?? "");
    const mode = block.getFieldValue("VALUE_MODE");
    if (mode === "literal") {
      action.value = value;
    } else if (mode === "json") {
      try {
        action.value = JSON.parse(value);
      } catch {
        throw new Error(`O valor JSON literal de '${action.name}' é inválido.`);
      }
    } else {
      action.valueSource = nonEmpty(value);
    }
  }

  function addConditionValue(condition, side, mode, data, actionName) {
    if (mode === "source") {
      condition[`${side}Source`] = nonEmpty(data);
      return;
    }

    if (mode === "json") {
      try {
        condition[`${side}Value`] = JSON.parse(String(data ?? ""));
      } catch {
        throw new Error(
          `O ${side === "left" ? "lado esquerdo" : "lado direito"} JSON de ` +
          `'${actionName}' é inválido.`);
      }
      return;
    }

    condition[`${side}Value`] = String(data ?? "");
  }

  function parseJsonField(value, label, actionName) {
    try {
      return JSON.parse(String(value ?? ""));
    } catch {
      throw new Error(`O ${label} JSON de '${actionName}' é inválido.`);
    }
  }

  function addArtifactDestination(action, block) {
    const directoryMode = block.getFieldValue("DIRECTORY_MODE");
    const directoryData = nonEmpty(block.getFieldValue("DIRECTORY_DATA"));
    if (directoryMode === "source") {
      if (!directoryData) {
        throw new Error(`Informe o caminho que fornece a pasta de '${action.name}'.`);
      }
      action.destinationDirectorySource = directoryData;
    } else if (directoryMode === "literal") {
      if (!directoryData) {
        throw new Error(`Informe a pasta literal de '${action.name}'.`);
      }
      action.destinationDirectory = directoryData;
    }

    const fileMode = block.getFieldValue("FILE_MODE");
    const fileData = nonEmpty(block.getFieldValue("FILE_DATA"));
    if (fileMode === "source") {
      if (!fileData) {
        throw new Error(`Informe o caminho que fornece o nome do arquivo de '${action.name}'.`);
      }
      action.fileNameSource = fileData;
    } else if (fileMode === "literal") {
      if (!fileData) {
        throw new Error(`Informe o nome do arquivo de '${action.name}'.`);
      }
      action.fileName = fileData;
    }

    action.separateByExecution =
      block.getFieldValue("SEPARATE_EXECUTION") === "TRUE";
    action.conflictStrategy = block.getFieldValue("CONFLICT");
  }

  function addRequestConfiguration(action, block) {
    action.method = block.getFieldValue("METHOD");
    action.bodyType = block.getFieldValue("BODY_TYPE");

    const bodyMode = block.getFieldValue("BODY_MODE");
    const bodyData = block.getFieldValue("BODY_DATA");
    if (bodyMode === "source") {
      action.requestBodySource = nonEmpty(bodyData);
    } else if (bodyMode === "literal") {
      action.requestBody = String(bodyData ?? "");
    } else if (bodyMode === "json") {
      action.requestBody = parseJsonField(bodyData, "corpo", action.name);
    }

    const headersMode = block.getFieldValue("HEADERS_MODE");
    const headersData = block.getFieldValue("HEADERS_DATA");
    if (headersMode === "source") {
      action.requestHeadersSource = nonEmpty(headersData);
    } else if (headersMode === "json") {
      const headers = parseJsonField(headersData, "objeto de cabeçalhos", action.name);
      if (!headers || Array.isArray(headers) || typeof headers !== "object") {
        throw new Error(`Os cabeçalhos JSON de '${action.name}' devem formar um objeto.`);
      }
      action.requestHeaders = headers;
    }
  }

  function blockToAction(block, counter) {
    const index = counter.value++;
    const action = {
      id: actionId(block, index),
      name: nonEmpty(block.getFieldValue("NAME"))
    };

    switch (block.type) {
      case "rpa_navigate":
        action.type = "navigate";
        addValue(action, block);
        break;
      case "rpa_click":
        action.type = "click";
        addLocator(action, block);
        break;
      case "rpa_click_optional":
        action.type = "clickIfVisible";
        addLocator(action, block);
        action.timeoutMs = Number(block.getFieldValue("TIMEOUT"));
        break;
      case "rpa_wait":
        action.type = "wait";
        addLocator(action, block);
        action.state = block.getFieldValue("STATE");
        action.optional = block.getFieldValue("OPTIONAL") === "TRUE";
        if (Number(block.getFieldValue("TIMEOUT")) > 0) {
          action.timeoutMs = Number(block.getFieldValue("TIMEOUT"));
        }
        if (block.getFieldValue("MATCH_MODE") !== "first") {
          action.matchMode = block.getFieldValue("MATCH_MODE");
        }
        break;
      case "rpa_fill":
        action.type = "fill";
        addLocator(action, block);
        addValue(action, block);
        break;
      case "rpa_select_option":
        action.type = "selectOption";
        addLocator(action, block);
        addValue(action, block);
        action.optionMode = block.getFieldValue("OPTION_MODE");
        break;
      case "rpa_set_checked":
        action.type = "setChecked";
        addLocator(action, block);
        addValue(action, block);
        break;
      case "rpa_press_key":
        action.type = "pressKey";
        addLocator(action, block);
        addValue(action, block);
        break;
      case "rpa_type_sequentially":
        action.type = "typeSequentially";
        addLocator(action, block);
        addValue(action, block);
        action.delayMs = Number(block.getFieldValue("DELAY"));
        action.clearFirst = block.getFieldValue("CLEAR_FIRST") === "TRUE";
        action.blurAfter = block.getFieldValue("BLUR_AFTER") === "TRUE";
        break;
      case "rpa_type_across_inputs":
        action.type = "typeAcrossInputs";
        addLocator(action, block);
        addValue(action, block);
        action.delayMs = Number(block.getFieldValue("DELAY"));
        action.clearFirst = block.getFieldValue("CLEAR_FIRST") === "TRUE";
        action.blurAfter = block.getFieldValue("BLUR_AFTER") === "TRUE";
        break;
      case "rpa_click_new_page":
        action.type = "clickAndSwitchPage";
        addLocator(action, block);
        action.readySelector = nonEmpty(block.getFieldValue("READY_SELECTOR"));
        break;
      case "rpa_switch_page":
        action.type = "switchPage";
        addValue(action, block);
        action.property = block.getFieldValue("PROPERTY");
        action.comparison = block.getFieldValue("COMPARISON");
        action.readySelector = nonEmpty(block.getFieldValue("READY_SELECTOR"));
        break;
      case "rpa_close_page":
        action.type = "closePage";
        action.readySelector = nonEmpty(block.getFieldValue("READY_SELECTOR"));
        break;
      case "rpa_upload":
        action.type = "upload";
        addLocator(action, block);
        addValue(action, block);
        action.optional = block.getFieldValue("OPTIONAL") === "TRUE";
        break;
      case "rpa_wait_stable":
        action.type = "waitStable";
        break;
      case "rpa_preserve_fill":
        action.type = "preserveOrFill";
        addLocator(action, block);
        addValue(action, block);
        action.comparison = block.getFieldValue("COMPARISON");
        break;
      case "rpa_select2":
        action.type = "select2";
        addLocator(action, block);
        addValue(action, block);
        action.triggerSelector = nonEmpty(block.getFieldValue("TRIGGER_SELECTOR"));
        action.optionSelector = nonEmpty(block.getFieldValue("OPTION_SELECTOR"));
        if (block.getFieldValue("COMPARISON") !== "legacy") {
          action.comparison = block.getFieldValue("COMPARISON");
        }
        break;
      case "rpa_currency":
        action.type = "fillMaskedCurrency";
        addLocator(action, block);
        addValue(action, block);
        if (Number(block.getFieldValue("DECIMAL_PLACES")) !== 2) {
          action.decimalPlaces = Number(block.getFieldValue("DECIMAL_PLACES"));
        }
        if (Number(block.getFieldValue("DELAY")) !== 30) {
          action.delayMs = Number(block.getFieldValue("DELAY"));
        }
        if (block.getFieldValue("COMMIT_KEY") !== "Tab") {
          action.commitKey = block.getFieldValue("COMMIT_KEY");
        }
        break;
      case "rpa_fail":
        action.type = "fail";
        addValue(action, block);
        break;
      case "rpa_complete_authentication_attempt":
        action.type = "completeAuthenticationAttempt";
        break;
      case "rpa_transform_path":
        action.type = "transformPath";
        addValue(action, block);
        action.operation = block.getFieldValue("OPERATION");
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        break;
      case "rpa_set_variable":
        action.type = "setVariable";
        addValue(action, block);
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        break;
      case "rpa_capture_timestamp":
        action.type = "captureTimestamp";
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        break;
      case "rpa_wait_one_time_code":
        action.type = "waitForOneTimeCode";
        action.providerAlias = nonEmpty(block.getFieldValue("PROVIDER_ALIAS"));
        action.notBeforeSource = nonEmpty(block.getFieldValue("NOT_BEFORE_SOURCE"));
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        action.timeoutMs = Number(block.getFieldValue("TIMEOUT_MS"));
        action.pollIntervalMs = Number(block.getFieldValue("POLL_INTERVAL_MS"));
        break;
      case "rpa_read_element":
        action.type = "readElement";
        addLocator(action, block);
        action.property = block.getFieldValue("PROPERTY");
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        if (action.property === "attribute") {
          action.attribute = nonEmpty(block.getFieldValue("ATTRIBUTE"));
        }
        break;
      case "rpa_read_elements":
        action.type = "readElements";
        addLocator(action, block);
        action.property = block.getFieldValue("PROPERTY");
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        action.maxItems = Number(block.getFieldValue("MAX_ITEMS"));
        if (action.property === "attribute") {
          action.attribute = nonEmpty(block.getFieldValue("ATTRIBUTE"));
        }
        break;
      case "rpa_screenshot":
        action.type = "screenshot";
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        addArtifactDestination(action, block);
        break;
      case "rpa_download_click":
        action.type = "download";
        action.downloadMode = "click";
        addLocator(action, block);
        action.timeoutMs = Number(block.getFieldValue("TIMEOUT"));
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        addArtifactDestination(action, block);
        break;
      case "rpa_download_request":
        action.type = "download";
        action.downloadMode = "request";
        addValue(action, block);
        addRequestConfiguration(action, block);
        action.timeoutMs = Number(block.getFieldValue("TIMEOUT"));
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        addArtifactDestination(action, block);
        break;
      case "rpa_safe_final":
        action.type = "safeFinalConfirmation";
        addLocator(action, block);
        if (block.getFieldValue("VALIDATE_COMPLETION") === "TRUE") {
          action.successSelector = nonEmpty(block.getFieldValue("SUCCESS_SELECTOR"));
          action.successText = nonEmpty(block.getFieldValue("SUCCESS_TEXT"));
          action.protocolSelector = nonEmpty(block.getFieldValue("PROTOCOL_SELECTOR"));
          action.protocolPattern = nonEmpty(block.getFieldValue("PROTOCOL_PATTERN"));
          action.completionTarget = nonEmpty(block.getFieldValue("COMPLETION_TARGET"));
          action.confirmationMessageTarget = nonEmpty(
            block.getFieldValue("CONFIRMATION_MESSAGE_TARGET"));
          action.protocolTarget = nonEmpty(block.getFieldValue("PROTOCOL_TARGET"));
          action.timeoutMs = Number(block.getFieldValue("TIMEOUT"));
        }
        action.target = nonEmpty(block.getFieldValue("TARGET"));
        addArtifactDestination(action, block);
        break;
      case "rpa_if_value": {
        action.type = "if";
        const operator = block.getFieldValue("OPERATOR");
        action.condition = {
          type: "value",
          operator,
          ignoreCase: block.getFieldValue("IGNORE_CASE") === "TRUE"
        };
        addConditionValue(
          action.condition,
          "left",
          block.getFieldValue("LEFT_MODE"),
          block.getFieldValue("LEFT_DATA"),
          action.name);
        if (!['isEmpty', 'isNotEmpty'].includes(operator)) {
          addConditionValue(
            action.condition,
            "right",
            block.getFieldValue("RIGHT_MODE"),
            block.getFieldValue("RIGHT_DATA"),
            action.name);
        }
        action.actions = blockChainToActions(block.getInputTargetBlock("THEN"), counter);
        action.elseActions = blockChainToActions(block.getInputTargetBlock("ELSE"), counter);
        break;
      }
      case "rpa_if_element":
        action.type = "if";
        action.condition = {
          type: "element",
          state: block.getFieldValue("STATE")
        };
        addLocator(action.condition, block, action.name);
        if (block.getFieldValue("MATCH_MODE") !== "first") {
          action.condition.matchMode = block.getFieldValue("MATCH_MODE");
        }
        action.actions = blockChainToActions(block.getInputTargetBlock("THEN"), counter);
        action.elseActions = blockChainToActions(block.getInputTargetBlock("ELSE"), counter);
        break;
      case "rpa_repeat":
        action.type = "repeat";
        if (block.getFieldValue("COUNT_MODE") === "source") {
          action.timesSource = nonEmpty(block.getFieldValue("COUNT_DATA"));
        } else {
          action.times = Number(block.getFieldValue("COUNT_DATA"));
        }
        if (block.getFieldValue("INDEX_VARIABLE") !== "repeatIndex") {
          action.indexVariable = nonEmpty(block.getFieldValue("INDEX_VARIABLE"));
        }
        action.actions = blockChainToActions(block.getInputTargetBlock("DO"), counter);
        break;
      case "rpa_for_each":
        action.type = "forEach";
        if (block.getFieldValue("ITEMS_MODE") === "source") {
          action.itemsSource = nonEmpty(block.getFieldValue("ITEMS_DATA"));
        } else {
          const parsedItems = JSON.parse(block.getFieldValue("ITEMS_DATA") || "[]");
          if (!Array.isArray(parsedItems)) {
            throw new Error(`A lista literal de '${action.name}' deve ser um array JSON.`);
          }
          action.items = parsedItems;
        }
        action.itemVariable = nonEmpty(block.getFieldValue("ITEM_VARIABLE"));
        action.indexVariable = nonEmpty(block.getFieldValue("INDEX_VARIABLE"));
        action.actions = blockChainToActions(block.getInputTargetBlock("DO"), counter);
        break;
      case "rpa_run_subflow":
        action.type = "runSubflow";
        action.subflow = nonEmpty(block.getFieldValue("SUBFLOW"));
        break;
      default:
        throw new Error(`Bloco não interpretado: ${block.type}`);
    }

    return action;
  }

  function blockChainToActions(firstBlock, counter) {
    const actions = [];
    let current = firstBlock;
    while (current) {
      actions.push(blockToAction(current, counter));
      current = current.getNextBlock();
    }
    return actions;
  }

  function allNestedActions(actions) {
    return actions.flatMap(action => [
      action,
      ...allNestedActions(action.actions || []),
      ...allNestedActions(action.elseActions || [])
    ]);
  }

  function findSubflowCycle(subflows) {
    const visiting = new Set();
    const visited = new Set();
    const normalized = Object.fromEntries(
      Object.entries(subflows).map(([name, actions]) => [name.toLowerCase(), actions]));

    function visit(name) {
      const normalizedName = name.toLowerCase();
      if (visited.has(normalizedName)) return null;
      if (visiting.has(normalizedName)) return name;
      visiting.add(normalizedName);
      const references = allNestedActions(normalized[normalizedName] || [])
        .filter(action => action.type === "runSubflow")
        .map(action => action.subflow);
      for (const reference of references) {
        const cycle = visit(reference);
        if (cycle) return cycle;
      }
      visiting.delete(normalizedName);
      visited.add(normalizedName);
      return null;
    }

    for (const name of Object.keys(subflows)) {
      const cycle = visit(name);
      if (cycle) return cycle;
    }
    return null;
  }

  function findSubflowDepthOverflow(subflows, maximumDepth = 32) {
    const normalized = Object.fromEntries(
      Object.entries(subflows).map(([name, actions]) => [name.toLowerCase(), actions]));
    const deepestSeen = new Map();
    const pending = Object.keys(normalized).map(name => ({
      name,
      depth: 1,
      path: [name]
    }));

    while (pending.length) {
      const current = pending.pop();
      if (current.depth > maximumDepth) return current.path;
      if ((deepestSeen.get(current.name) ?? 0) >= current.depth) continue;
      deepestSeen.set(current.name, current.depth);

      const references = allNestedActions(normalized[current.name] || [])
        .filter(action => action.type === "runSubflow")
        .map(action => String(action.subflow ?? "").toLowerCase())
        .filter(reference => normalized[reference] && !current.path.includes(reference));
      for (const reference of references) {
        pending.push({
          name: reference,
          depth: current.depth + 1,
          path: [...current.path, reference]
        });
      }
    }

    return null;
  }

  function readFlow() {
    const roots = workspace.getTopBlocks(true);
    const definitions = roots.filter(block => block.type === "rpa_subflow_definition");
    const mainRoots = roots.filter(block => block.type !== "rpa_subflow_definition");
    if (mainRoots.length !== 1) {
      return {
        flow: null,
        error: "Mantenha uma única sequência principal; definições de subfluxo devem ficar separadas."
      };
    }

    const counter = { value: 0 };
    const actions = blockChainToActions(mainRoots[0], counter);
    const subflows = {};
    for (const definition of definitions) {
      const name = nonEmpty(definition.getFieldValue("SUBFLOW"));
      if (!name || !/^[A-Za-z][A-Za-z0-9_.-]*$/.test(name)) {
        return { flow: null, error: "Todo subfluxo deve possuir um nome válido e único." };
      }
      if (Object.keys(subflows).some(existing => existing.toLowerCase() === name.toLowerCase())) {
        return { flow: null, error: `O subfluxo '${name}' está duplicado.` };
      }
      subflows[name] = blockChainToActions(
        definition.getInputTargetBlock("ACTIONS"),
        counter);
      if (!subflows[name].length) {
        return { flow: null, error: `O subfluxo '${name}' precisa possuir ao menos uma ação.` };
      }
    }

    const flattened = [
      ...allNestedActions(actions),
      ...Object.values(subflows).flatMap(allNestedActions)
    ];
    const missing = flattened.find(action =>
      !action.name ||
      (["click", "clickIfVisible", "wait", "fill", "selectOption", "setChecked", "pressKey", "typeSequentially", "typeAcrossInputs", "clickAndSwitchPage", "upload", "preserveOrFill", "select2", "fillMaskedCurrency", "readElement", "readElements", "safeFinalConfirmation"].includes(action.type) && !action.selector) ||
      (action.type === "download" && action.downloadMode === "click" && !action.selector) ||
      (action.type === "fail" && action.value === undefined && !action.valueSource) ||
      (action.type === "transformPath" &&
        action.value === undefined && !action.valueSource) ||
      (action.type === "download" && action.downloadMode === "request" &&
        !action.valueSource && !nonEmpty(action.value)) ||
      (action.type === "switchPage" &&
        action.value === undefined && !action.valueSource));
    if (missing) {
      return { flow: null, error: `Preencha nome, seletor ou URL da ação '${missing.id}'.` };
    }

    const identifiers = new Set();
    for (const action of flattened) {
      if (identifiers.has(action.id.toLowerCase())) {
        return { flow: null, error: `O ID de ação '${action.id}' está duplicado.` };
      }
      identifiers.add(action.id.toLowerCase());

      if (action.type === "if" &&
          !(action.actions?.length || action.elseActions?.length)) {
        return { flow: null, error: `A condição '${action.name}' não possui ações.` };
      }
      if (action.type === "if" && action.condition?.type === "value") {
        const condition = action.condition;
        const hasLeftValue = hasOwn(condition, "leftValue");
        const hasLeftSource = Boolean(condition.leftSource);
        if (hasLeftValue === hasLeftSource ||
            (hasLeftSource && !dataPathPattern.test(condition.leftSource))) {
          return {
            flow: null,
            error: `Informe exatamente um lado esquerdo válido na condição '${action.name}'.`
          };
        }

        if (!["isEmpty", "isNotEmpty"].includes(condition.operator)) {
          const hasRightValue = hasOwn(condition, "rightValue");
          const hasRightSource = Boolean(condition.rightSource);
          if (hasRightValue === hasRightSource ||
              (hasRightSource && !dataPathPattern.test(condition.rightSource))) {
            return {
              flow: null,
              error: `Informe exatamente um lado direito válido na condição '${action.name}'.`
            };
          }
        }
      }
      if (action.type === "if" && action.condition?.type === "element" &&
          !action.condition.selector) {
        return { flow: null, error: `Preencha o seletor da condição '${action.name}'.` };
      }
      const locator = action.type === "if" && action.condition?.type === "element"
        ? action.condition
        : action;
      const invalidSource = invalidLocatorSource(locator);
      if (invalidSource) {
        return {
          flow: null,
          error: `A origem '${invalidSource}' do localizador de '${action.name}' não é suportada.`
        };
      }
      const typeAcrossInputsError = typeAcrossInputsValidationError(action);
      if (typeAcrossInputsError) {
        return { flow: null, error: typeAcrossInputsError };
      }
      const safeFinalConfirmationError =
        safeFinalConfirmationValidationError(action);
      if (safeFinalConfirmationError) {
        return { flow: null, error: safeFinalConfirmationError };
      }
      if (["repeat", "forEach"].includes(action.type) && !action.actions?.length) {
        return { flow: null, error: `O bloco '${action.name}' não possui ações internas.` };
      }
      if (action.type === "repeat") {
        const hasTimes = action.times !== undefined;
        const hasTimesSource = Boolean(action.timesSource);
        if (hasTimes === hasTimesSource ||
            (hasTimesSource && !dataPathPattern.test(action.timesSource))) {
          return {
            flow: null,
            error: `A repetição '${action.name}' exige quantidade literal ou origem válida.`
          };
        }
      }
      if (action.type === "repeat" && action.times !== undefined &&
          (!Number.isInteger(action.times) || action.times < 0 || action.times > 1000000)) {
        return { flow: null, error: `A repetição '${action.name}' exige um inteiro entre 0 e 1000000.` };
      }
      if (action.type === "repeat" && action.indexVariable &&
          !/^[A-Za-z][A-Za-z0-9_-]*$/.test(action.indexVariable)) {
        return { flow: null, error: `O nome temporário de '${action.name}' é inválido.` };
      }
      if (action.type === "forEach") {
        const hasItems = Array.isArray(action.items);
        const hasItemsSource = Boolean(action.itemsSource);
        if (hasItems === hasItemsSource ||
            (hasItemsSource && !dataPathPattern.test(action.itemsSource))) {
          return {
            flow: null,
            error: `A lista de '${action.name}' exige um array literal ou origem válida.`
          };
        }
      }
      if (action.type === "forEach" &&
          (!/^[A-Za-z][A-Za-z0-9_-]*$/.test(action.itemVariable || "") ||
           !/^[A-Za-z][A-Za-z0-9_-]*$/.test(action.indexVariable || ""))) {
        return { flow: null, error: `O nome temporário de '${action.name}' é inválido.` };
      }
      if (["setVariable", "transformPath", "readElement", "readElements", "captureTimestamp", "waitForOneTimeCode"].includes(action.type) &&
          !runtimeTargetPattern.test(action.target || "")) {
        return { flow: null, error: `O destino de '${action.name}' deve usar runtime.<caminho>.` };
      }
      if (action.type === "waitForOneTimeCode") {
        if (!providerAliasPattern.test(action.providerAlias || "")) {
          return {
            flow: null,
            error: `O provider de '${action.name}' deve possuir um alias válido.`
          };
        }
        if (!dataPathPattern.test(action.notBeforeSource || "")) {
          return {
            flow: null,
            error: `A origem temporal de '${action.name}' deve ser um caminho de dados válido.`
          };
        }
        if (!Number.isInteger(action.timeoutMs) ||
            action.timeoutMs < 1000 || action.timeoutMs > 600000) {
          return {
            flow: null,
            error: `O timeout de '${action.name}' deve ser um inteiro entre 1000 e 600000 ms.`
          };
        }
        if (!Number.isInteger(action.pollIntervalMs) ||
            action.pollIntervalMs < 500 || action.pollIntervalMs > 60000) {
          return {
            flow: null,
            error: `O intervalo de consulta de '${action.name}' deve ser um inteiro entre 500 e 60000 ms.`
          };
        }
        if (action.pollIntervalMs > action.timeoutMs) {
          return {
            flow: null,
            error: `O intervalo de consulta de '${action.name}' não pode exceder o timeout.`
          };
        }
      }
      if (action.type === "transformPath" &&
          !["fileName", "fileNameWithoutExtension", "extension", "directoryName"]
            .includes(action.operation)) {
        return { flow: null, error: `A transformação de caminho '${action.name}' é inválida.` };
      }
      if (["download", "screenshot", "safeFinalConfirmation"].includes(action.type) &&
          action.target &&
          !/^runtime\.[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z][A-Za-z0-9_-]*)*$/.test(action.target)) {
        return { flow: null, error: `O destino de '${action.name}' deve usar runtime.<caminho>.` };
      }
      if (action.type === "download" && action.downloadMode === "request" &&
          action.bodyType === "form" && typeof action.requestBody === "string") {
        return { flow: null, error: `O corpo de formulário de '${action.name}' deve ser um objeto JSON ou vir de um caminho.` };
      }
      if (["readElement", "readElements"].includes(action.type) &&
          action.property === "attribute" && !action.attribute) {
        return { flow: null, error: `Informe o atributo que será lido em '${action.name}'.` };
      }
    }

    const finalActions = flattened.filter(action => action.type === "safeFinalConfirmation");
    const finalIndex = actions.findIndex(action => action.type === "safeFinalConfirmation");
    if (finalActions.length > 1 ||
        (finalActions.length === 1 && finalIndex !== actions.length - 1)) {
      return {
        flow: null,
        error: "A confirmação final segura deve existir no máximo uma vez e ser o último bloco principal."
      };
    }

    const calls = flattened.filter(action => action.type === "runSubflow");
    const unknownCall = calls.find(action =>
      !Object.keys(subflows).some(name => name.toLowerCase() === action.subflow?.toLowerCase()));
    if (unknownCall) {
      return { flow: null, error: `Subfluxo não encontrado: '${unknownCall.subflow}'.` };
    }

    const cycle = findSubflowCycle(subflows);
    if (cycle) {
      return { flow: null, error: `Ciclo detectado entre subfluxos envolvendo '${cycle}'.` };
    }

    const excessiveSubflowPath = findSubflowDepthOverflow(subflows);
    if (excessiveSubflowPath) {
      return {
        flow: null,
        error: "A cadeia de subfluxos ultrapassa o limite de 32 chamadas aninhadas."
      };
    }

    return {
      flow: {
        schemaVersion: 1,
        name: loadedFlowName,
        inputs: structuredClone(loadedFlowInputs),
        actions,
        subflows
      },
      error: null
    };
  }

  function refreshJson(event) {
    if (event?.isUiEvent) return;
    try {
      const result = readFlow();
      generatedJson.textContent = result.flow ? JSON.stringify(result.flow, null, 2) : "";
      const actionCount = result.flow
        ? allNestedActions([
            ...result.flow.actions,
            ...Object.values(result.flow.subflows).flat()
          ]).length
        : 0;
      validationMessage.textContent = result.error || `${actionCount} ações estruturais prontas para o runtime .NET.`;
      validationMessage.classList.toggle("error", Boolean(result.error));
    } catch (error) {
      generatedJson.textContent = "";
      validationMessage.textContent = error.message;
      validationMessage.classList.add("error");
    }
  }

  function download(contents, filename) {
    const blob = new Blob([contents], { type: "application/json;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  async function saveProductionFlow() {
    const result = readFlow();
    if (result.error) throw new Error(result.error);

    if (serverSession) {
      return apiRequest("/api/flow", {
        method: "PUT",
        body: result.flow
      });
    }

    const contents = `${JSON.stringify(result.flow, null, 2)}\n`;

    if (window.showSaveFilePicker) {
      const handle = await window.showSaveFilePicker({
        suggestedName: serverSession?.flowFile || "flow.production.json",
        types: [{ description: "Fluxo JSON", accept: { "application/json": [".json"] } }]
      });
      const writable = await handle.createWritable();
      await writable.write(contents);
      await writable.close();
      return;
    }

    download(contents, serverSession?.flowFile || "flow.production.json");
  }

  function exportWorkspace() {
    const state = Blockly.serialization.workspaces.save(workspace);
    const baseName = (editorProfile?.displayName || "rpa")
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .replace(/[^A-Za-z0-9]+/g, "-")
      .replace(/^-|-$/g, "")
      .toLowerCase();
    download(`${JSON.stringify(state, null, 2)}\n`, `${baseName || "rpa"}-workspace.json`);
  }

  async function importWorkspace(file) {
    const state = JSON.parse(await file.text());
    workspace.clear();
    Blockly.serialization.workspaces.load(state, workspace);
    refreshJson();
  }

  async function importFlow(file) {
    loadProductionFlow(JSON.parse(await file.text()));
  }

  async function apiRequest(path, options = {}) {
    const headers = new Headers(options.headers || {});
    if (serverSession?.token) {
      headers.set("X-Editor-Token", serverSession.token);
    }

    const request = { ...options, headers, cache: "no-store" };
    if (options.body && typeof options.body !== "string") {
      headers.set("Content-Type", "application/json");
      request.body = JSON.stringify(options.body);
    }

    const response = await fetch(path, request);
    if (!response.ok) {
      let message = `Falha HTTP ${response.status}.`;
      try {
        const error = await response.json();
        message = error.error || message;
      } catch {
        // Mantém a mensagem HTTP quando a resposta não for JSON.
      }
      throw new Error(message);
    }

    return response.status === 204 ? null : response.json();
  }

  function getConfigurationValue(path) {
    return path.split(".").reduce((current, part) => current?.[part], loadedConfiguration);
  }

  function setConfigurationValue(path, value) {
    const parts = path.split(".");
    let current = loadedConfiguration;
    for (let index = 0; index < parts.length - 1; index++) {
      current[parts[index]] ??= {};
      current = current[parts[index]];
    }
    current[parts.at(-1)] = value;
  }

  function renderConfigurationFields() {
    configurationFields.replaceChildren();
    for (const definition of configurationFieldDefinitions) {
      const label = document.createElement("label");
      label.className = "configuration-field";
      label.append(document.createTextNode(definition.label));

      if (definition.source) {
        const source = document.createElement("code");
        source.textContent = definition.source;
        label.append(source);
      }

      const input = document.createElement(
        definition.type === "stringList" ? "textarea" : "input");
      input.dataset.configPath = definition.path;
      input.dataset.nullable = definition.nullable ? "true" : "false";
      input.dataset.configType = definition.type;
      if (definition.type !== "stringList") {
        input.type = definition.type;
      }
      const value = getConfigurationValue(definition.path);
      if (definition.type === "checkbox") {
        input.checked = Boolean(value);
      } else if (definition.type === "stringList") {
        input.value = value == null ? "" : JSON.stringify(value, null, 2);
      } else {
        input.value = value ?? "";
      }
      label.append(input);
      configurationFields.append(label);
    }
  }

  function renderVariables() {
    variablesList.replaceChildren();
    const variables = loadedConfiguration?.Blockly?.Variables || {};
    const entries = Object.entries(variables)
      .sort(([first], [second]) => first.localeCompare(second, "pt-BR"));
    if (!entries.length) {
      const empty = document.createElement("p");
      empty.className = "empty-variables";
      empty.textContent = "Nenhuma variável personalizada cadastrada.";
      variablesList.append(empty);
      return;
    }

    for (const [key, value] of entries) {
      const row = document.createElement("div");
      row.className = "variable-row";
      row.dataset.variableKey = key;

      const keyLabel = document.createElement("label");
      keyLabel.append(document.createTextNode("Chave"));
      const keyInput = document.createElement("input");
      keyInput.className = "variable-key";
      keyInput.value = key;
      keyLabel.append(keyInput);

      const typeLabel = document.createElement("label");
      typeLabel.append(document.createTextNode("Tipo"));
      const typeSelect = document.createElement("select");
      typeSelect.className = "variable-type";
      for (const [label, optionValue] of [
        ["Texto", "string"],
        ["Lista JSON", "array"],
        ["Objeto JSON", "object"],
        ["Número", "number"],
        ["Booleano", "boolean"],
        ["Nulo", "null"]
      ]) {
        const option = document.createElement("option");
        option.value = optionValue;
        option.textContent = label;
        typeSelect.append(option);
      }
      typeSelect.value = variableType(value);
      typeLabel.append(typeSelect);

      const valueLabel = document.createElement("label");
      valueLabel.append(document.createTextNode("Valor"));
      const valueInput = document.createElement("input");
      valueInput.className = "variable-value";
      valueInput.type = /password|senha|secret|token/i.test(key) ? "password" : "text";
      valueInput.value = formatVariableValue(value);
      configureVariableInput(typeSelect.value, valueInput);
      typeSelect.addEventListener("change", () =>
        configureVariableInput(typeSelect.value, valueInput));
      valueLabel.append(valueInput);

      const removeButton = document.createElement("button");
      removeButton.type = "button";
      removeButton.className = "secondary";
      removeButton.textContent = "Remover";
      removeButton.addEventListener("click", () => {
        try {
          const keyToRemove = keyInput.value.trim();
          collectVariables();
          delete loadedConfiguration.Blockly.Variables[keyToRemove];
          renderVariables();
        } catch (error) {
          showConfigurationMessage(error.message, true);
        }
      });

      row.append(keyLabel, typeLabel, valueLabel, removeButton);
      variablesList.append(row);
    }
  }

  function variableType(value) {
    if (value === null) return "null";
    if (Array.isArray(value)) return "array";
    if (typeof value === "object") return "object";
    return typeof value === "number" || typeof value === "boolean"
      ? typeof value
      : "string";
  }

  function formatVariableValue(value) {
    if (value === null) return "";
    return typeof value === "object" ? JSON.stringify(value) : String(value);
  }

  function configureVariableInput(type, input) {
    input.disabled = type === "null";
    input.placeholder = type === "array"
      ? '[{"id": 1, "arquivos": []}]'
      : type === "object"
        ? '{"id": 1, "itens": []}'
        : "valor";
  }

  function parseVariableValue(type, rawValue, key) {
    switch (type) {
      case "string":
        return rawValue;
      case "number": {
        const number = Number(rawValue);
        if (!rawValue.trim() || !Number.isFinite(number)) {
          throw new Error(`A variável '${key}' exige um número válido.`);
        }
        return number;
      }
      case "boolean":
        if (!["true", "false"].includes(rawValue.trim().toLowerCase())) {
          throw new Error(`A variável '${key}' exige true ou false.`);
        }
        return rawValue.trim().toLowerCase() === "true";
      case "null":
        return null;
      case "array": {
        let parsed;
        try {
          parsed = JSON.parse(rawValue);
        } catch {
          throw new Error(`A variável '${key}' exige uma lista JSON válida.`);
        }
        if (!Array.isArray(parsed)) {
          throw new Error(`A variável '${key}' exige uma lista JSON válida.`);
        }
        return parsed;
      }
      case "object": {
        let parsed;
        try {
          parsed = JSON.parse(rawValue);
        } catch {
          throw new Error(`A variável '${key}' exige um objeto JSON válido.`);
        }
        if (parsed === null || Array.isArray(parsed) || typeof parsed !== "object") {
          throw new Error(`A variável '${key}' exige um objeto JSON válido.`);
        }
        return parsed;
      }
      default:
        throw new Error(`Tipo não suportado para a variável '${key}'.`);
    }
  }

  function collectVariables() {
    const variables = {};
    const renamedSources = new Map();
    for (const row of variablesList.querySelectorAll(".variable-row")) {
      const key = row.querySelector(".variable-key").value.trim();
      if (!/^[A-Za-z][A-Za-z0-9_.-]*$/.test(key)) {
        throw new Error(
          `A chave '${key}' é inválida. Use letras, números, ponto, hífen ou sublinhado.`);
      }

      if (Object.keys(variables).some(existing =>
        existing.toLowerCase() === key.toLowerCase())) {
        throw new Error(`A chave '${key}' está repetida.`);
      }

      variables[key] = parseVariableValue(
        row.querySelector(".variable-type").value,
        row.querySelector(".variable-value").value,
        key);
      const previousKey = row.dataset.variableKey;
      if (previousKey !== key) {
        renamedSources.set(`variables.${previousKey}`.toLowerCase(), `variables.${key}`);
        renamedSources.set(`config.${previousKey}`.toLowerCase(), `config.${key}`);
      }
      row.dataset.variableKey = key;
    }
    loadedConfiguration.Blockly.Variables = variables;
    renameVariableReferences(renamedSources);
  }

  function renameVariableReferences(renamedSources) {
    if (!renamedSources.size) return;

    for (const block of workspace.getAllBlocks(false)) {
      for (const fieldName of [
        "VALUE_SOURCE",
        "NOT_BEFORE_SOURCE",
        "SCOPE_HAS_TEXT_SOURCE",
        "HAS_TEXT_SOURCE"
      ]) {
        const sourceField = block.getField(fieldName);
        if (!sourceField) continue;
        const current = String(sourceField.getValue() || "");
        const replacement = renamedSources.get(current.toLowerCase());
        if (replacement) sourceField.setValue(replacement);
      }

      for (const [modeName, dataName] of [
        ["VALUE_MODE", "VALUE_DATA"],
        ["LEFT_MODE", "LEFT_DATA"],
        ["RIGHT_MODE", "RIGHT_DATA"],
        ["COUNT_MODE", "COUNT_DATA"],
        ["ITEMS_MODE", "ITEMS_DATA"]
      ]) {
        if (block.getFieldValue(modeName) === "source") {
          const sourceInput = block.getField(dataName);
          const current = String(sourceInput?.getValue() || "");
          const replacement = renamedSources.get(current.toLowerCase());
          if (replacement) sourceInput.setValue(replacement);
        }
      }
    }
  }

  function collectConfiguration() {
    for (const input of configurationFields.querySelectorAll("[data-config-path]")) {
      let value;
      if (input.type === "checkbox") {
        value = input.checked;
      } else if (input.type === "number") {
        value = Number(input.value);
      } else if (input.dataset.configType === "stringList") {
        if (input.dataset.nullable === "true" && !input.value.trim()) {
          value = null;
        } else {
          try {
            value = JSON.parse(input.value);
          } catch {
            throw new Error(
              `A configuração '${input.dataset.configPath}' exige uma lista JSON válida.`);
          }
          if (!Array.isArray(value) || value.some(item => typeof item !== "string")) {
            throw new Error(
              `A configuração '${input.dataset.configPath}' exige uma lista JSON de textos.`);
          }
        }
      } else if (input.dataset.nullable === "true" && !input.value.trim()) {
        value = null;
      } else {
        value = input.value;
      }
      setConfigurationValue(input.dataset.configPath, value);
    }

    collectVariables();
    return loadedConfiguration;
  }

  function showConfigurationMessage(message, isError = false) {
    configurationMessage.textContent = message;
    configurationMessage.classList.toggle("error", isError);
  }

  async function saveConfiguration() {
    if (!serverSession || !loadedConfiguration) {
      throw new Error("A configuração só pode ser salva pelo microservidor ASP.NET Core.");
    }

    const result = await apiRequest("/api/configuration", {
      method: "PUT",
      body: collectConfiguration()
    });
    showConfigurationMessage(`Configuração salva. Backup: ${result.backupFile}.`);
    return result;
  }

  async function connectServer() {
    const response = await fetch("/api/session", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("Microservidor ASP.NET Core não encontrado.");
    }

    serverSession = await response.json();
    editorProfile = serverSession.profile;
    if (!editorProfile?.displayName || !Array.isArray(editorProfile.configurationFields)) {
      throw new Error("O microservidor retornou um perfil de RPA inválido.");
    }
    configurationFieldDefinitions = editorProfile.configurationFields;
    const [configuration, flow] = await Promise.all([
      apiRequest("/api/configuration"),
      apiRequest("/api/flow")
    ]);
    loadedConfiguration = configuration;
    loadedConfiguration.Blockly ??= {};
    loadedConfiguration.Blockly.Variables ??= {};
    defaultFlow = structuredClone(flow);
    document.title = `Editor visual do fluxo - ${editorProfile.displayName}`;
    document.getElementById("editor-title").textContent =
      `Fluxo de ${editorProfile.displayName} em blocos`;
    document.getElementById("reset-flow").textContent = "Restaurar fluxo salvo";
    renderConfigurationFields();
    renderVariables();
    configurationFileLabel.textContent =
      `Arquivo carregado: ${serverSession.configurationFile}. Senhas não são copiadas para o fluxo.`;
    loadProductionFlow(flow);
    serverStatus.textContent = `Conectado: ${serverSession.configurationFile}`;
    serverStatus.classList.add("connected");
  }

  function addVariable() {
    try {
      collectVariables();
    } catch (error) {
      showConfigurationMessage(error.message, true);
      return;
    }

    const key = newVariableKey.value.trim();
    let value;
    try {
      value = parseVariableValue(
        newVariableType.value,
        newVariableValue.value,
        key || "nova variável");
    } catch (error) {
      showConfigurationMessage(error.message, true);
      return;
    }
    if (!/^[A-Za-z][A-Za-z0-9_.-]*$/.test(key)) {
      showConfigurationMessage(
        "A chave deve começar com uma letra e usar apenas letras, números, ponto, hífen ou sublinhado.",
        true);
      return;
    }

    if (Object.keys(loadedConfiguration.Blockly.Variables).some(existing =>
      existing.toLowerCase() === key.toLowerCase())) {
      showConfigurationMessage(`A variável '${key}' já existe.`, true);
      return;
    }

    loadedConfiguration.Blockly.Variables[key] = value;
    newVariableKey.value = "";
    newVariableType.value = "string";
    newVariableValue.value = "";
    renderVariables();
    showConfigurationMessage(`Variável '${key}' adicionada. Salve a configuração para persistir.`);
  }

  document.getElementById("reset-flow").addEventListener("click", () => loadProductionFlow(defaultFlow));
  document.getElementById("save-flow").addEventListener("click", async () => {
    try {
      await saveProductionFlow();
      validationMessage.textContent = "Fluxo de produção salvo.";
      validationMessage.classList.remove("error");
    } catch (error) {
      if (error.name !== "AbortError") {
        validationMessage.textContent = error.message;
        validationMessage.classList.add("error");
      }
    }
  });
  document.getElementById("save-all").addEventListener("click", async () => {
    try {
      await saveConfiguration();
      await saveProductionFlow();
      validationMessage.textContent = "Configuração e fluxo salvos pelo microservidor.";
      validationMessage.classList.remove("error");
    } catch (error) {
      validationMessage.textContent = error.message;
      validationMessage.classList.add("error");
    }
  });
  document.getElementById("open-configuration").addEventListener("click", () => {
    if (!loadedConfiguration) {
      validationMessage.textContent = "Abra o editor pelo atalho para conectar ao microservidor.";
      validationMessage.classList.add("error");
      return;
    }
    renderConfigurationFields();
    renderVariables();
    configurationDialog.showModal();
  });
  document.getElementById("save-configuration").addEventListener("click", async () => {
    try {
      await saveConfiguration();
    } catch (error) {
      showConfigurationMessage(error.message, true);
    }
  });
  document.getElementById("add-variable").addEventListener("click", addVariable);
  newVariableType.addEventListener("change", () =>
    configureVariableInput(newVariableType.value, newVariableValue));
  document.getElementById("import-flow").addEventListener("click", () => flowFile.click());
  document.getElementById("export-workspace").addEventListener("click", exportWorkspace);
  document.getElementById("import-workspace").addEventListener("click", () => workspaceFile.click());
  document.getElementById("copy-json").addEventListener("click", async event => {
    await navigator.clipboard.writeText(generatedJson.textContent);
    const button = event.currentTarget;
    button.textContent = "Copiado";
    window.setTimeout(() => { button.textContent = "Copiar JSON"; }, 1200);
  });
  flowFile.addEventListener("change", async () => {
    if (!flowFile.files?.length) return;
    try {
      await importFlow(flowFile.files[0]);
    } catch (error) {
      validationMessage.textContent = `Fluxo inválido: ${error.message}`;
      validationMessage.classList.add("error");
    } finally {
      flowFile.value = "";
    }
  });
  workspaceFile.addEventListener("change", async () => {
    if (!workspaceFile.files?.length) return;
    try {
      await importWorkspace(workspaceFile.files[0]);
    } catch (error) {
      validationMessage.textContent = `Workspace inválido: ${error.message}`;
      validationMessage.classList.add("error");
    } finally {
      workspaceFile.value = "";
    }
  });

  if (new URLSearchParams(window.location.search).has("roundtrip-test")) {
    window.RpaFlowEditorTesting = {
      roundTrip(flow) {
        loadProductionFlow(structuredClone(flow));
        const result = readFlow();
        if (result.error) {
          throw new Error(result.error);
        }
        return structuredClone(result.flow);
      },
      toolboxBlockTypes() {
        return createToolbox().contents
          .flatMap(category => category.contents)
          .map(item => item.type);
      },
      toolboxCategoryBlockTypes(categoryName) {
        const category = createToolbox().contents
          .find(item => item.name === categoryName);
        return category?.contents.map(item => item.type) ?? [];
      }
    };
  }

  workspace.addChangeListener(refreshJson);
  connectServer().catch(error => {
    serverStatus.textContent = "Modo local sem backend";
    serverStatus.classList.add("error");
    validationMessage.textContent = `${error.message} O fluxo ainda pode ser exportado manualmente.`;
    validationMessage.classList.add("error");
    loadProductionFlow(defaultFlow);
  });
})();
