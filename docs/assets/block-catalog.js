(() => {
  "use strict";

  const property = (
    json,
    label,
    required,
    format,
    description,
    options = [],
    defaultValue = "—") => ({
      json,
      label,
      required,
      format,
      description,
      options,
      defaultValue
    });

  const actionFields = [
    property("id", "ID da ação", "Sim", "identificador", "É o apelido técnico exclusivo desta etapa. Use algo que descreva a tarefa, como preencher-cnpj ou clicar-enviar. Nenhuma outra ação do fluxo pode repetir o mesmo ID, nem mesmo dentro de condições, repetições ou subfluxos. Comece por uma letra e use apenas letras, números, ponto, hífen ou sublinhado."),
    property("name", "Nome visível", "Sim", "texto", "É a frase que a pessoa verá no Blockly, no console e nos registros de execução, por exemplo Preencher CNPJ. Escreva um nome curto e claro. Mudar este nome não muda o comportamento do bloco; ele serve para leitura e diagnóstico."),
    property("type", "Tipo da ação", "Automático", "opção interna", "Informa ao interpretador C# qual trabalho deve ser executado. O editor preenche esse campo automaticamente quando você escolhe o bloco. Para trocar o comportamento, substitua o bloco no Blockly; não edite este valor à mão.")
  ];

  const locatorFields = [
    property("selector", "Seletor CSS", "Sim", "CSS", "É a regra usada para encontrar o botão, campo ou outro elemento da página. Exemplo: input[name='cnpj']. Prefira ID, name, data-* ou outro atributo que represente a função do elemento. Evite classes geradas, posições como nth-child e caminhos longos, pois costumam quebrar quando a tela muda."),
    property("scope", "Área onde procurar", "Não", "CSS", "Limita a procura a uma parte da tela, como um formulário, uma janela ou uma linha de tabela. Exemplo: use tr[data-id='123'] como área e button[data-action='edit'] como seletor para clicar no botão daquela linha. Preencha este campo sempre que usar um texto do escopo."),
    property("scopeHasText", "Texto fixo da área", "Não", "texto", "Use quando existem várias áreas parecidas e o texto que identifica a área é sempre o mesmo, como Cliente Maria. O RPA primeiro escolhe a área com esse texto e só depois procura o elemento dentro dela. Não preencha junto com Origem do texto da área."),
    property("scopeHasTextSource", "Origem do texto da área", "Não", "caminho de dados", "Use quando o texto que identifica a área muda em cada caso. Informe de onde ele vem, por exemplo input.nomeCliente ou loop.nota.numero. Não preencha junto com Texto fixo da área. Se o caminho não fornecer valor, a execução para para evitar agir na área errada."),
    property("frameSelectors", "Caminho de iframes", "Não", "lista JSON", "Preencha somente quando o elemento fica dentro de um iframe, que é uma página incorporada dentro da página principal. Informe os seletores do iframe mais externo até o mais interno, por exemplo [\"iframe#sistema\"]. Deixe a lista vazia quando não houver iframe.", ["0 a 8 seletores"], "[]"),
    property("hasText", "Texto fixo do elemento", "Não", "texto", "Ajuda a escolher o elemento correto quando o seletor encontra vários parecidos. Exemplo: selector button e texto Enviar. Use apenas quando o texto é sempre igual. Não preencha junto com Origem do texto do elemento."),
    property("hasTextSource", "Origem do texto do elemento", "Não", "caminho de dados", "Use quando o texto que identifica o elemento muda em cada execução. Informe um caminho como input.numeroNota. Não preencha junto com Texto fixo do elemento. Se o caminho estiver vazio, o RPA para em vez de ampliar a procura e correr o risco de usar o elemento errado.")
  ];

  const valueFields = [
    property("value", "Valor fixo", "Um dos dois", "valor JSON", "Use quando o bloco deve receber sempre o mesmo valor, por exemplo Ativo, 10 ou true. O valor fica gravado dentro do fluxo. Preencha este campo ou Origem do valor, nunca os dois."),
    property("valueSource", "Origem do valor", "Um dos dois", "caminho de dados", "Use quando o valor muda de um caso para outro. Informe onde buscá-lo, como input.cnpj, config.url, attachments.pdf, runtime.codigo, system.workItemId ou loop.nota. Preencha este campo ou Valor fixo, nunca os dois.")
  ];

  const artifactFields = [
    property("destinationDirectory", "Pasta fixa de destino", "Não", "caminho", "Use quando todos os arquivos desta ação devem ir para a mesma pasta. Pode ser uma pasta abaixo do output padrão ou um caminho absoluto/UNC autorizado, como \\\\servidor\\compartilhamento. Não preencha junto com Origem da pasta."),
    property("destinationDirectorySource", "Origem da pasta", "Não", "caminho de dados", "Use quando a pasta muda conforme o caso. Informe um caminho como input.pastaDestino ou config.pastaRelatorios. Não preencha junto com Pasta fixa de destino. Um caminho relativo continua preso ao diretório de output por segurança."),
    property("fileName", "Nome fixo do arquivo", "Depende da ação", "nome de arquivo", "Define um nome que será sempre usado, como comprovante.pdf. Não preencha junto com Origem do nome. Em alguns downloads, este campo pode ficar vazio para conservar o nome enviado pelo próprio site."),
    property("fileNameSource", "Origem do nome do arquivo", "Depende da ação", "caminho de dados", "Use quando o nome muda por caso. Informe onde buscá-lo, por exemplo input.nomeArquivo ou runtime.numeroProtocolo. Não preencha junto com Nome fixo do arquivo."),
    property("separateByExecution", "Criar uma pasta por execução", "Não", "sim ou não", "Quando true, cria uma subpasta com o identificador da execução. Isso impede que arquivos de casos diferentes se misturem. Deixe true no uso normal; use false somente quando uma integração exigir uma pasta compartilhada específica.", ["true: sim", "false: não"], "true"),
    property("conflictStrategy", "O que fazer se o arquivo já existir", "Não", "opção", "Escolhe o comportamento quando já existe um arquivo com o mesmo nome. unique cria outro nome e preserva o anterior; fail interrompe para você investigar; overwrite substitui o arquivo existente e só deve ser usado quando essa perda for intencional.", ["unique: cria outro nome", "fail: interrompe", "overwrite: substitui deliberadamente"], "unique"),
    property("target", "Guardar o caminho gerado em", "Não", "runtime.*", "Depois de gravar o arquivo com sucesso, salva o caminho completo em uma variável temporária, por exemplo runtime.comprovante. Essa variável pode ser usada por blocos posteriores e por mapeamentos de saída. Não aceita input.*, config.* nem uma posição de lista.")
  ];

  const block = definition => ({
    capabilities: [],
    useWhen: [],
    avoidWhen: [],
    safety: [],
    failures: [],
    ...definition
  });

  const catalog = [
    block({
      blockType: "rpa_navigate",
      actionType: "navigate",
      title: "Navegar",
      category: "Navegação",
      capabilities: ["web"],
      summary: "Abre uma URL e aguarda o DOM inicial ficar disponível.",
      useWhen: ["Iniciar o acesso ao sistema.", "Trocar explicitamente de endereço dentro do mesmo contexto de página."],
      avoidWhen: ["A navegação acontece como consequência de um clique; nesse caso, use clicar ou clicar e assumir nova aba."],
      properties: [
        ...actionFields,
        ...valueFields,
        property("timeoutMs", "Tempo máximo", "Não", "100 a 600000 milissegundos", "É quanto tempo o RPA pode esperar a abertura inicial da página antes de considerar que houve falha. Por exemplo, 30000 significa 30 segundos. Normalmente o editor usa a configuração geral; este campo só é necessário quando esta navegação precisa de um prazo diferente.")
      ],
      example: { id: "abrir-sistema", type: "navigate", name: "Abrir sistema", valueSource: "input.url" },
      safety: ["URLs vindas de input devem ser validadas administrativamente; o bloco não concede autorização para qualquer domínio."],
      failures: ["URL ausente ou inválida.", "Timeout antes de DOMContentLoaded.", "Falha de rede ou certificado."]
    }),
    block({
      blockType: "rpa_click",
      actionType: "click",
      title: "Clicar",
      category: "Navegação",
      capabilities: ["web"],
      summary: "Clica em exatamente um elemento visível.",
      useWhen: ["Botões, links e opções obrigatórias com alvo estável e único."],
      avoidWhen: ["Elementos opcionais.", "A confirmação final de um processo real.", "Alvos ambíguos que só funcionam com First ou Nth."],
      properties: [...actionFields, ...locatorFields],
      example: { id: "abrir-formulario", type: "click", name: "Abrir formulário", selector: "button[data-action='new']" },
      safety: ["Não use como substituto de safeFinalConfirmation.", "Não force o clique para ignorar overlay ou estado desabilitado."],
      failures: ["Nenhum alvo visível.", "Mais de um alvo visível.", "Elemento coberto, desabilitado ou removido durante a ação."]
    }),
    block({
      blockType: "rpa_click_optional",
      actionType: "clickIfVisible",
      title: "Clicar se visível",
      category: "Navegação",
      capabilities: ["web"],
      summary: "Tenta localizar um elemento por um prazo curto e segue quando ele realmente não aparece.",
      useWhen: ["Banner de cookies, aviso eventual ou modal verdadeiramente opcional."],
      avoidWhen: ["Passos obrigatórios; tornar um passo opcional apenas esconde a causa da falha."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("timeoutMs", "Tempo de procura", "Não", "100 a 600000 milissegundos", "Define por quanto tempo procurar o elemento opcional. Por exemplo, 2000 significa 2 segundos. Se ele não aparecer nesse prazo, o fluxo continua normalmente; erros como seletor inválido ou mais de um resultado continuam interrompendo a execução.", [], "2000")
      ],
      example: { id: "fechar-banner", type: "clickIfVisible", name: "Fechar banner", selector: "button[data-action='close-banner']", timeoutMs: 1500 },
      safety: ["Somente timeout é tolerado; seletor inválido, ambiguidade e outros erros continuam interrompendo."],
      failures: ["Mais de um alvo quando o elemento aparece.", "Erro diferente de timeout."]
    }),
    block({
      blockType: "rpa_wait",
      actionType: "wait",
      title: "Aguardar elemento",
      category: "Esperas",
      capabilities: ["web"],
      summary: "Espera um estado observável do DOM sem usar pausa fixa.",
      useWhen: ["Aguardar elemento aparecer, ser anexado, ficar oculto ou sair do DOM."],
      avoidWhen: ["A página precisa estabilizar rede, loaders e formulário em conjunto; use Aguardar página estável."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("state", "Estado que deve ser alcançado", "Sim", "opção", "Escolha visible para esperar o elemento aparecer na tela; attached para ele existir no código da página, mesmo oculto; hidden para ficar invisível ou deixar de existir; e detached para deixar de existir completamente.", ["visible: visível", "attached: presente na página", "hidden: oculto ou ausente", "detached: ausente da página"]),
        property("optional", "Pode continuar se não acontecer?", "Não", "sim ou não", "Quando true, o fluxo continua se apenas o tempo de espera terminar. Quando false, o fim do prazo interrompe o caso. Um seletor inválido ou ambíguo continua sendo erro nos dois modos.", ["true: sim", "false: não"], "false"),
        property("timeoutMs", "Tempo máximo", "Não", "0 ou 100 a 600000 milissegundos", "Informa quanto tempo esperar pelo estado desejado. Por exemplo, 10000 significa 10 segundos. Use 0 para não definir um prazo próprio e aproveitar o tempo padrão configurado no runtime.", [], "0"),
        property("matchMode", "Como tratar vários resultados", "Não", "opção", "Use single em fluxos novos: ele exige que a quantidade encontrada seja coerente e revela seletores ambíguos. first usa apenas o primeiro resultado e existe para compatibilidade com fluxos antigos; pode esconder que a página passou a ter elementos repetidos.", ["single: exigir resultado sem ambiguidade", "first: usar o primeiro resultado"], "single em bloco novo")
      ],
      example: { id: "aguardar-formulario", type: "wait", name: "Aguardar formulário", selector: "form[data-ready='true']", state: "visible", optional: false, matchMode: "single", timeoutMs: 30000 },
      safety: ["Não confunda attached com pronto para interação.", "Prefira single para novos fluxos."],
      failures: ["Estado não atingido no prazo.", "Cardinalidade inválida no modo single."]
    }),
    block({
      blockType: "rpa_click_new_page",
      actionType: "clickAndSwitchPage",
      title: "Clicar e assumir nova aba",
      category: "Navegação",
      capabilities: ["web"],
      summary: "Arma a espera por uma nova página antes do clique e muda o contexto do restante do fluxo.",
      useWhen: ["Um clique abre relatório, detalhe ou sistema em outra aba/janela."],
      avoidWhen: ["A navegação ocorre na mesma aba.", "A aba já existe; use Assumir aba existente."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("readySelector", "Sinal de que a nova aba está pronta", "Sim", "CSS", "Informe um elemento único que só aparece quando a tela correta da nova aba já carregou, por exemplo main[data-page='report']. O RPA só continua depois de encontrar esse sinal."),
        property("timeoutMs", "Tempo máximo", "Não", "100 a 600000 milissegundos", "É o prazo total para a nova aba abrir e o sinal de prontidão aparecer. Por exemplo, 30000 significa 30 segundos. Se o prazo terminar, a ação falha e não continua em uma aba incerta.")
      ],
      example: { id: "abrir-relatorio", type: "clickAndSwitchPage", name: "Abrir relatório", selector: "a[data-report]", readySelector: "main[data-page='report']" },
      safety: ["A espera é armada antes do clique para não perder eventos rápidos."],
      failures: ["Nenhuma nova aba.", "Mais de uma aba inesperada.", "readySelector ausente ou ambíguo."]
    }),
    block({
      blockType: "rpa_switch_page",
      actionType: "switchPage",
      title: "Assumir aba existente",
      category: "Navegação",
      capabilities: ["web"],
      summary: "Seleciona exatamente uma página aberta por URL ou título.",
      useWhen: ["A aba já foi aberta por uma ação anterior ou pelo próprio sistema."],
      avoidWhen: ["O clique que cria a aba ainda será executado; use Clicar e assumir nova aba."],
      properties: [
        ...actionFields,
        ...valueFields,
        property("property", "O que identificar na aba", "Sim", "opção", "Escolha url para procurar pelo endereço da página ou title para procurar pelo título mostrado na aba do navegador. Use a informação mais estável e exclusiva no sistema automatizado.", ["url: endereço", "title: título da aba"]),
        property("comparison", "Como comparar", "Sim", "opção", "Escolha exact quando o valor precisa ser totalmente igual; caseInsensitive quando maiúsculas e minúsculas não importam; ou contains quando basta localizar um trecho do endereço ou título.", ["exact: exatamente igual", "caseInsensitive: igual sem diferenciar maiúsculas", "contains: contém o trecho"]),
        property("readySelector", "Sinal de que a aba escolhida está pronta", "Não", "CSS", "Informe um elemento que confirme que o RPA assumiu a tela certa, por exemplo main[data-page='home']. Se preenchido, o fluxo só continua quando esse elemento estiver disponível sem ambiguidade.")
      ],
      example: { id: "voltar-ao-sistema", type: "switchPage", name: "Assumir sistema", property: "title", comparison: "contains", value: "Sistema", readySelector: "main" },
      safety: ["A ação falha se zero ou mais de uma aba corresponder; não escolhe silenciosamente pela ordem."],
      failures: ["Aba não encontrada.", "Duas ou mais abas correspondentes.", "Seletor de prontidão ausente."]
    }),
    block({
      blockType: "rpa_close_page",
      actionType: "closePage",
      title: "Fechar aba atual",
      category: "Navegação",
      capabilities: ["web"],
      summary: "Fecha a página atual e assume a última página restante.",
      useWhen: ["Fechar relatório ou detalhe e voltar à aba principal."],
      avoidWhen: ["Existe somente uma aba aberta."],
      properties: [
        ...actionFields,
        property("readySelector", "Sinal da aba para a qual o RPA voltará", "Não", "CSS", "Depois de fechar a aba atual, o RPA assume uma das abas restantes. Informe um elemento único da tela esperada, por exemplo main[data-page='home'], para impedir que o fluxo continue na aba errada.")
      ],
      example: { id: "fechar-relatorio", type: "closePage", name: "Fechar relatório", readySelector: "main[data-page='home']" },
      safety: ["A única aba do contexto nunca é fechada por este bloco."],
      failures: ["Tentativa de fechar a única aba.", "Seletor da página restante ausente."]
    }),
    block({
      blockType: "rpa_wait_stable",
      actionType: "waitStable",
      title: "Aguardar página estável",
      category: "Esperas",
      capabilities: ["web"],
      summary: "Combina quietude de rede, ausência de loaders visíveis e estabilidade do formulário.",
      useWhen: ["Depois de AJAX, upload, seleção dependente ou mudança que atualiza vários campos."],
      avoidWhen: ["Existe um único sinal de DOM simples; Aguardar elemento é mais específico."],
      properties: [...actionFields],
      configuration: ["Runtime.ReadinessQuietPeriodMs", "Runtime.FormStabilityMs", "Runtime.BusySelectors"],
      example: { id: "aguardar-calculos", type: "waitStable", name: "Aguardar cálculos da página" },
      safety: ["Não substitua por sleep. Ajuste os sinais reais do sistema na configuração."],
      failures: ["Rede nunca fica quieta.", "Loader permanece visível.", "Formulário continua mudando."]
    }),
    block({
      blockType: "rpa_fill",
      actionType: "fill",
      title: "Preencher",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Substitui o conteúdo de um campo com o preenchimento padrão do Playwright.",
      useWhen: ["O RPA é a autoridade sobre o valor e o componente aceita preenchimento direto."],
      avoidWhen: ["A página pode preencher o campo.", "O componente exige eventos reais de teclado ou máscara."],
      properties: [...actionFields, ...locatorFields, ...valueFields],
      example: { id: "preencher-referencia", type: "fill", name: "Preencher referência", selector: "input[name='reference']", valueSource: "input.referencia" },
      safety: ["Use Preservar ou preencher quando o valor também puder vir do sistema."],
      failures: ["Alvo ausente ou duplicado.", "Campo não editável.", "Valor de origem inexistente."]
    }),
    block({
      blockType: "rpa_select_option",
      actionType: "selectOption",
      title: "Selecionar opção nativa",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Seleciona uma opção de um elemento select HTML nativo.",
      useWhen: ["O controle real é um select e a opção pode ser identificada por value, label ou índice."],
      avoidWhen: ["Select2 ou componente visual customizado; use o bloco Select2."],
      properties: [
        ...actionFields,
        ...locatorFields,
        ...valueFields,
        property("optionMode", "Como identificar a opção", "Sim", "opção", "Escolha value quando o dado corresponde ao valor interno do HTML; label quando corresponde ao texto que a pessoa vê; ou index quando só existe uma posição conhecida. O primeiro índice é 0. Prefira value ou label, porque a ordem das opções pode mudar.", ["value: valor interno", "label: texto exibido", "index: posição iniciada em 0"])
      ],
      example: { id: "selecionar-tipo", type: "selectOption", name: "Selecionar tipo", selector: "select[name='type']", value: "SERVICE", optionMode: "value" },
      safety: ["O índice só é seguro quando a ordem faz parte do contrato auditado."],
      failures: ["Nenhuma opção correspondente.", "Índice inválido.", "Elemento não é select."]
    }),
    block({
      blockType: "rpa_set_checked",
      actionType: "setChecked",
      title: "Definir marcação",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Marca ou desmarca checkbox/radio e confirma o estado final.",
      useWhen: ["O estado desejado é booleano e precisa ser idempotente."],
      avoidWhen: ["Um clique alterna estado desconhecido; setChecked é mais seguro que click."],
      properties: [...actionFields, ...locatorFields, ...valueFields],
      example: { id: "aceitar-termos", type: "setChecked", name: "Aceitar termos", selector: "input[name='terms']", value: true },
      safety: ["O valor deve ser booleano, não o texto 'true'."],
      failures: ["Elemento não marcável.", "Estado final diverge.", "Alvo ausente ou ambíguo."]
    }),
    block({
      blockType: "rpa_press_key",
      actionType: "pressKey",
      title: "Pressionar tecla",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Envia uma tecla ou combinação Playwright para um elemento visível.",
      useWhen: ["Confirmar com Enter, sair com Tab ou executar combinação suportada."],
      avoidWhen: ["Digitar texto completo; use Preencher ou Digitar sequencialmente."],
      properties: [...actionFields, ...locatorFields, ...valueFields],
      example: { id: "sair-do-campo", type: "pressKey", name: "Sair do campo", selector: "input[name='code']", value: "Tab" },
      safety: ["Use nomes de tecla aceitos pelo Playwright, como Tab, Enter e Control+A."],
      failures: ["Tecla inválida.", "Elemento não recebe foco.", "Alvo ambíguo."]
    }),
    block({
      blockType: "rpa_type_sequentially",
      actionType: "typeSequentially",
      title: "Digitar sequencialmente",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Digita caractere por caractere, com eventos reais, e verifica o valor final.",
      useWhen: ["Componente reativo, bloqueio de colagem ou máscara dependente de teclado."],
      avoidWhen: ["Campo comum que aceita Fill.", "Campo preenchido pelo JavaScript da página."],
      properties: [
        ...actionFields,
        ...locatorFields,
        ...valueFields,
        property("delayMs", "Intervalo entre caracteres", "Não", "0 a 1000 milissegundos", "É a pequena pausa entre uma tecla e outra. O valor 50 significa 0,05 segundo. Só aumente quando o componente do site perder caracteres ou não reagir à digitação rápida, pois valores altos deixam o RPA mais lento.", [], "50"),
        property("clearFirst", "Limpar o conteúdo anterior", "Não", "sim ou não", "Quando true, seleciona todo o conteúdo existente e o apaga antes de digitar. Quando false, escreve a partir da posição atual do cursor. Em formulários comuns, mantenha true para não juntar o novo valor ao antigo.", ["true: limpar", "false: conservar"], "true em bloco novo"),
        property("blurAfter", "Sair do campo ao terminar", "Não", "sim ou não", "Quando true, pressiona Tab depois da última tecla e espera a página estabilizar. Isso é útil em telas que só validam ou calculam o valor quando o usuário sai do campo. Use false apenas quando a próxima ação precisa manter o foco ali.", ["true: sair com Tab", "false: manter o foco"], "true em bloco novo")
      ],
      example: { id: "digitar-token", type: "typeSequentially", name: "Digitar token", selector: "input[name='token']", valueSource: "runtime.token", delayMs: 80, clearFirst: true, blurAfter: true },
      safety: ["O runtime não registra o valor digitado, pois ele pode ser sensível."],
      failures: ["Valor final diferente.", "Campo recriado sem poder ser relocalizado.", "Quantidade de eventos insuficiente."]
    }),
    block({
      blockType: "rpa_type_across_inputs",
      actionType: "typeAcrossInputs",
      title: "Digitar em inputs segmentados",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Distribui os caracteres entre vários inputs visíveis e confirma a concatenação.",
      useWhen: ["PIN, token ou código dividido em uma caixa por caractere."],
      avoidWhen: ["Existe um único input; use Digitar sequencialmente."],
      properties: [
        ...actionFields,
        ...locatorFields,
        ...valueFields,
        property("delayMs", "Intervalo entre caracteres", "Não", "0 a 1000 milissegundos", "É a pausa entre cada caractere enviado ao próximo campo. O valor 50 significa 0,05 segundo. Aumente somente se a tela perder dígitos, pois a pausa é aplicada a todos eles.", [], "50"),
        property("clearFirst", "Limpar todos os campos antes", "Não", "sim ou não", "Quando true, apaga o conteúdo de cada campo visível antes de distribuir os caracteres. Isso evita misturar o código novo com valores antigos. Use false somente quando conservar o conteúdo anterior fizer parte da regra.", ["true: limpar", "false: conservar"], "true"),
        property("blurAfter", "Sair do último campo ao terminar", "Não", "sim ou não", "Quando true, pressiona Tab no último campo e espera a página estabilizar. Isso imita a saída do usuário e dispara validações que dependem da perda de foco. Use false se o sistema avançar sozinho e não aceitar Tab.", ["true: sair com Tab", "false: manter o foco"], "true")
      ],
      example: { id: "digitar-pin", type: "typeAcrossInputs", name: "Digitar PIN", selector: "div.pin input", valueSource: "runtime.pin", delayMs: 60, clearFirst: true, blurAfter: true },
      safety: ["A quantidade de inputs visíveis deve ser exatamente igual à quantidade de caracteres."],
      failures: ["Cardinalidade diferente.", "Valor concatenado divergente.", "Inputs recriados de forma incompatível."]
    }),
    block({
      blockType: "rpa_upload",
      actionType: "upload",
      title: "Anexar arquivo",
      category: "Formulários",
      capabilities: ["web", "filesystem"],
      summary: "Anexa um arquivo em input[type=file], valida existência e aguarda estabilidade.",
      useWhen: ["Enviar PDF, XML, planilha ou outro anexo já resolvido pelo caso."],
      avoidWhen: ["O caminho ainda não foi validado ou o efeito de upload não é autorizado."],
      properties: [
        ...actionFields,
        property("selector", "Seletor do campo de arquivo", "Sim", "CSS", "Deve apontar para o elemento HTML input[type='file'] que recebe o arquivo, mesmo que esse elemento esteja invisível atrás de um botão como Anexar. Não use o seletor do botão visual quando ele não for o campo real de upload."),
        ...valueFields,
        property("optional", "Pode continuar sem o anexo?", "Não", "sim ou não", "Quando true, o fluxo continua se a origem do arquivo estiver vazia. Quando false, a falta do caminho interrompe o caso. Se um caminho foi informado, mas o arquivo não existe, a execução sempre falha, pois isso indica dado incorreto.", ["true: pode continuar sem arquivo", "false: arquivo obrigatório"], "false")
      ],
      example: { id: "anexar-documento", type: "upload", name: "Anexar documento", selector: "input[type='file'][name='document']", valueSource: "attachments.documento", optional: false },
      safety: ["O arquivo é resolvido em relação à pasta da configuração e deve existir antes da interação."],
      failures: ["Caminho ausente quando obrigatório.", "Arquivo inexistente.", "Página não estabiliza após upload."]
    }),
    block({
      blockType: "rpa_preserve_fill",
      actionType: "preserveOrFill",
      title: "Preservar ou preencher",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Preenche somente quando vazio; preserva valor equivalente e falha diante de divergência.",
      useWhen: ["O portal pode calcular ou preencher o campo depois de upload/AJAX."],
      avoidWhen: ["O RPA deve sempre substituir o conteúdo; use Preencher."],
      properties: [
        ...actionFields,
        ...locatorFields,
        ...valueFields,
        property("comparison", "Como comparar os valores", "Sim", "opção", "Escolha exact para exigir texto exatamente igual; caseInsensitive para ignorar diferença entre maiúsculas e minúsculas; ou currency para comparar como valor monetário, tolerando apenas diferenças de formatação como R$ e separadores.", ["exact: texto exatamente igual", "caseInsensitive: texto sem diferenciar maiúsculas", "currency: valor monetário"])
      ],
      example: { id: "validar-total", type: "preserveOrFill", name: "Validar total", selector: "input[name='total']", valueSource: "input.total", comparison: "currency" },
      safety: ["Nunca sobrescreve silenciosamente um valor diferente já calculado pela página."],
      failures: ["Valor existente divergente.", "Campo ausente ou duplicado.", "Valor esperado não resolvido."]
    }),
    block({
      blockType: "rpa_select2",
      actionType: "select2",
      title: "Selecionar opção Select2",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Interage com o select nativo e as opções renderizadas de um componente Select2.",
      useWhen: ["O sistema usa Select2 ou componente compatível auditado."],
      avoidWhen: ["Select HTML nativo.", "Tentativa de injetar opção ou chamar endpoint interno."],
      properties: [
        ...actionFields,
        ...locatorFields,
        ...valueFields,
        property("triggerSelector", "Elemento que abre a lista", "Sim", "CSS", "Informe o seletor do controle visível em que a pessoa clicaria para abrir as opções do Select2. Ele costuma ser diferente do select oculto usado internamente pela página."),
        property("optionSelector", "Elementos que representam as opções", "Sim", "CSS", "Informe um seletor que encontre as opções mostradas depois que a lista abre, por exemplo .select2-results__option. O bloco usará o texto e a regra de comparação para escolher exatamente uma delas."),
        property("comparison", "Como comparar a opção", "Não", "opção", "Escolha exact para texto exatamente igual; caseInsensitive para ignorar maiúsculas e minúsculas; ou numeric quando valores como 001 e 1 devem representar o mesmo número. Fluxos antigos sem esse campo mantêm o comportamento anterior.", ["exact: texto igual", "caseInsensitive: texto sem diferenciar maiúsculas", "numeric: mesmo valor numérico"], "compatibilidade legada")
      ],
      example: { id: "selecionar-unidade", type: "select2", name: "Selecionar unidade", selector: "select[name='unit']", triggerSelector: "#unit + .select2", optionSelector: ".select2-results__option", valueSource: "input.unidade", comparison: "caseInsensitive" },
      safety: ["Não injeta option e não chama o endpoint AJAX por fora do componente."],
      failures: ["Controle já contém valor divergente.", "Opção não encontrada.", "Lista não estabiliza."]
    }),
    block({
      blockType: "rpa_currency",
      actionType: "fillMaskedCurrency",
      title: "Preencher campo monetário",
      category: "Formulários",
      capabilities: ["web"],
      summary: "Digita unidades menores em campo mascarado e valida o número formatado.",
      useWhen: ["Máscara monetária depende de eventos de teclado."],
      avoidWhen: ["Campo aceita preenchimento direto sem máscara."],
      properties: [
        ...actionFields,
        ...locatorFields,
        ...valueFields,
        property("decimalPlaces", "Quantidade de casas decimais", "Não", "0 a 6", "Informa quantas casas existem depois da vírgula. Para reais e centavos, use 2: o valor 123,45 será digitado como a sequência de centavos esperada pelo componente. Só mude se a moeda ou o campo usar outra precisão.", [], "2"),
        property("delayMs", "Intervalo entre dígitos", "Não", "0 a 1000 milissegundos", "É a pausa entre um dígito e outro. O valor 30 significa 0,03 segundo. Aumente somente quando a máscara monetária perder números ou formatá-los incorretamente.", [], "30"),
        property("commitKey", "Tecla usada para confirmar", "Não", "opção", "Escolha Tab quando o site valida o valor ao sair do campo ou Enter quando ele exige confirmação explícita. Após essa tecla, o bloco aguarda a página estabilizar antes de seguir.", ["Tab: sair do campo", "Enter: confirmar no campo"], "Tab")
      ],
      example: { id: "preencher-valor", type: "fillMaskedCurrency", name: "Preencher valor", selector: "input[name='amount']", valueSource: "input.valor", decimalPlaces: 2, delayMs: 30, commitKey: "Tab" },
      safety: ["Valor já preenchido e numericamente equivalente é preservado."],
      failures: ["Valor formatado diverge.", "Número inválido.", "Máscara não responde aos eventos."]
    }),
    block({
      blockType: "rpa_fail",
      actionType: "fail",
      title: "Interromper com erro",
      category: "Dados e controle",
      summary: "Encerra o fluxo imediatamente com mensagem literal ou dinâmica.",
      useWhen: ["Estado terminal, autenticação recusada, pré-condição ausente ou resposta incompatível."],
      avoidWhen: ["Representar sucesso ou simplesmente registrar informação."],
      properties: [...actionFields, ...valueFields],
      example: { id: "falhar-autenticacao", type: "fail", name: "Interromper autenticação", value: "O sistema recusou as credenciais." },
      safety: ["Não interage com o sistema nem altera input/configuração."],
      failures: ["A própria ação sempre encerra a execução.", "Origem da mensagem ausente."]
    }),
    block({
      blockType: "rpa_transform_path",
      actionType: "transformPath",
      title: "Transformar caminho",
      category: "Dados e controle",
      summary: "Extrai partes textuais de um caminho sem acessar o sistema de arquivos.",
      useWhen: ["Obter nome, nome sem extensão, extensão ou diretório para usar em outra ação."],
      avoidWhen: ["Validar existência, copiar ou mover arquivo; este bloco não faz I/O."],
      properties: [
        ...actionFields,
        ...valueFields,
        property("operation", "Parte do caminho que deseja obter", "Sim", "opção", "Escolha fileName para obter o nome com extensão, como nota.pdf; fileNameWithoutExtension para obter apenas nota; extension para obter .pdf; ou directoryName para obter somente a pasta que contém o arquivo.", ["fileName: nome com extensão", "fileNameWithoutExtension: nome sem extensão", "extension: extensão", "directoryName: pasta"]),
        property("target", "Variável que receberá o resultado", "Sim", "runtime.*", "Informe uma variável temporária, como runtime.nomeArquivo. Depois desta ação, os próximos blocos podem usar esse valor. O caminho deve começar com runtime. e não pode apontar para uma posição numérica de lista.")
      ],
      example: { id: "obter-nome", type: "transformPath", name: "Obter nome do anexo", valueSource: "attachments.documento", operation: "fileName", target: "runtime.documento.nome" },
      safety: ["Aceita caminhos locais e UNC, mas não verifica se eles existem."],
      failures: ["Operação inválida.", "Origem ausente.", "Destino fora de runtime.*."]
    }),
    block({
      blockType: "rpa_set_variable",
      actionType: "setVariable",
      title: "Definir variável",
      category: "Dados e controle",
      summary: "Copia um valor JSON tipado para runtime.*.",
      useWhen: ["Guardar estado intermediário, objeto, lista, flag ou valor calculado para etapas posteriores."],
      avoidWhen: ["Tentar alterar input.*, config.*, attachments.*, system.* ou loop.*."],
      properties: [
        ...actionFields,
        ...valueFields,
        property("target", "Variável que receberá o valor", "Sim", "runtime.*", "Informe um nome como runtime.cliente.cnpj. O bloco cria automaticamente as partes intermediárias, neste exemplo runtime.cliente, se ainda não existirem. Não use input. ou config., porque esses dados são tratados como entrada, nem uma posição numérica de lista.")
      ],
      example: { id: "marcar-inicio", type: "setVariable", name: "Marcar início", value: true, target: "runtime.etapas.iniciada" },
      safety: ["O valor é clonado; não compartilha referência mutável com o input."],
      failures: ["Destino inválido.", "Conflito com valor simples já existente no caminho.", "Literal e source simultâneos."]
    }),
    block({
      blockType: "rpa_capture_timestamp",
      actionType: "captureTimestamp",
      title: "Capturar instante UTC",
      category: "Dados e controle",
      summary: "Grava o instante UTC atual em formato round-trip do .NET.",
      useWhen: ["Marcar o momento anterior à solicitação de código, início de etapa ou correlação temporal."],
      avoidWhen: ["Esperar passagem de tempo; este bloco somente captura."],
      properties: [
        ...actionFields,
        property("target", "Variável que receberá o horário", "Sim", "runtime.*", "Informe uma variável como runtime.inicioEsperaOtp. Ela receberá a data e a hora atuais em um formato completo e sem ambiguidade, por exemplo 2026-07-30T15:56:47.0000000+00:00. Esse marco permite ignorar mensagens antigas ao buscar um código.")
      ],
      example: { id: "registrar-pedido-token", type: "captureTimestamp", name: "Registrar pedido do token", target: "runtime.autenticacao.solicitadoEm" },
      safety: ["Não acessa navegador nem serviço externo."],
      failures: ["Destino fora de runtime.*."]
    }),
    block({
      blockType: "rpa_wait_one_time_code",
      actionType: "waitForOneTimeCode",
      title: "Aguardar código de uso único",
      category: "Esperas",
      capabilities: ["oneTimeCode"],
      summary: "Solicita ao provedor injetado pelo host o código mais recente posterior a um marco temporal.",
      useWhen: ["MFA por e-mail, SMS ou outro canal implementado por provider seguro."],
      avoidWhen: ["O host não possui IOneTimeCodeProvider.", "Usar o código sem primeiro capturar o instante da solicitação."],
      properties: [
        ...actionFields,
        property("providerAlias", "Nome da configuração de e-mail", "Sim", "identificador", "É o apelido da configuração protegida cadastrada no host, por exemplo otp-principal. Ele diz qual caixa de e-mail e quais filtros usar. Não coloque tenant, secret, token ou senha dentro do fluxo."),
        property("notBeforeSource", "Horário a partir do qual procurar", "Sim", "caminho de dados", "Informe a variável criada pelo bloco Capturar horário, por exemplo runtime.inicioEsperaOtp. Mensagens recebidas antes desse instante serão ignoradas, evitando reutilizar um código antigo."),
        property("target", "Variável que receberá o código", "Sim", "runtime.*", "Informe onde guardar somente o código extraído, por exemplo runtime.codigoOtp. Um bloco de preenchimento posterior poderá usar essa variável como Origem do valor."),
        property("timeoutMs", "Tempo máximo de espera", "Sim", "1000 a 600000 milissegundos", "É o tempo total durante o qual o RPA procurará o e-mail antes de encerrar o caso como falha. Por exemplo, 120000 significa 2 minutos."),
        property("pollIntervalMs", "Intervalo entre consultas", "Sim", "500 a 60000 milissegundos", "Define de quanto em quanto tempo a caixa será consultada. Por exemplo, 3000 significa uma consulta a cada 3 segundos. Não pode ser maior que o tempo máximo e não deve ser muito baixo para evitar consultas desnecessárias.")
      ],
      example: { id: "aguardar-token", type: "waitForOneTimeCode", name: "Aguardar token", providerAlias: "email-otp", notBeforeSource: "runtime.autenticacao.solicitadoEm", target: "runtime.autenticacao.codigo", timeoutMs: 120000, pollIntervalMs: 5000 },
      safety: ["Credenciais do provedor ficam fora do fluxo.", "A consulta só começa quando a execução alcança este bloco.", "No worker incluído, o código é removido antes de persistir OutputJson e não pode ser mapeado como output ou artefato.", "Com claim ativo, OTP por e-mail exige MaxParallelism=1."],
      failures: ["Provider não configurado ou desabilitado.", "Permissão Mail.Read, tenant, aplicativo ou segredo incorretos.", "Assunto, remetente ou expressão não correspondem.", "Timeout sem código válido.", "Timestamp inválido.", "Polling fora dos limites."]
    }),
    block({
      blockType: "rpa_read_element",
      actionType: "readElement",
      title: "Ler elemento",
      category: "Leituras",
      capabilities: ["web"],
      summary: "Lê uma propriedade de exatamente um elemento e salva valor tipado em runtime.*.",
      useWhen: ["Capturar texto, value, checked ou atributo para decisão ou output."],
      avoidWhen: ["É esperada uma coleção; use Ler vários elementos."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("property", "O que deve ser lido", "Sim", "opção", "Escolha value para o conteúdo de um campo; text para o texto que aparece na tela; checked para saber se uma caixa está marcada, retornando true ou false; ou attribute para ler um atributo HTML específico.", ["value: conteúdo do campo", "text: texto visível", "checked: marcado ou desmarcado", "attribute: atributo HTML"]),
        property("attribute", "Nome do atributo HTML", "Somente ao escolher attribute", "texto", "Preencha apenas quando O que deve ser lido estiver em attribute. Informe o nome exato, como href, title ou data-id. Nos outros modos, deixe este campo vazio."),
        property("target", "Variável que receberá o dado", "Sim", "runtime.*", "Informe uma variável temporária, como runtime.protocolo. Ela receberá texto, true ou false, ou null, de acordo com a opção escolhida e com o que existir no elemento.")
      ],
      example: { id: "ler-protocolo", type: "readElement", name: "Ler protocolo", selector: "[data-field='protocol']", property: "text", target: "runtime.protocolo" },
      safety: ["A leitura exige elemento anexado e único."],
      failures: ["Alvo ausente ou duplicado.", "Propriedade inválida.", "attribute sem nome."]
    }),
    block({
      blockType: "rpa_read_elements",
      actionType: "readElements",
      title: "Ler vários elementos",
      category: "Leituras",
      capabilities: ["web"],
      summary: "Lê zero ou mais elementos em ordem e grava um array JSON.",
      useWhen: ["Coletar linhas, opções, mensagens ou valores repetidos."],
      avoidWhen: ["A semântica exige exatamente um alvo."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("property", "O que ler de cada elemento", "Sim", "opção", "A mesma leitura será aplicada a todos os elementos encontrados. Use value para conteúdo de campos, text para texto exibido, checked para marcação ou attribute para um atributo HTML específico.", ["value: conteúdo do campo", "text: texto visível", "checked: marcado ou desmarcado", "attribute: atributo HTML"]),
        property("attribute", "Nome do atributo HTML", "Somente ao escolher attribute", "texto", "Preencha apenas no modo attribute. Informe o nome exato que será lido em cada elemento, como href ou data-id. Nos outros modos, deixe vazio."),
        property("maxItems", "Quantidade máxima permitida", "Não", "1 a 10000", "É uma proteção contra seletores amplos demais. Se a página encontrar mais itens que esse limite, o RPA interrompe antes de coletar. Ajuste ao volume máximo legítimo do processo; não aumente apenas para esconder um seletor incorreto.", [], "1000"),
        property("target", "Lista que receberá os resultados", "Sim", "runtime.*", "Informe uma variável como runtime.linhas. Ela receberá uma lista na mesma ordem dos elementos encontrados. Se nenhum elemento corresponder, a lista será vazia, não um erro por si só.")
      ],
      example: { id: "ler-mensagens", type: "readElements", name: "Ler mensagens", selector: "ul.messages > li", property: "text", maxItems: 100, target: "runtime.mensagens" },
      safety: ["Falha antes de coletar quando a quantidade ultrapassa maxItems."],
      failures: ["Limite excedido.", "Propriedade inválida.", "Atributo obrigatório ausente."]
    }),
    block({
      blockType: "rpa_screenshot",
      actionType: "screenshot",
      title: "Salvar screenshot",
      category: "Artefatos",
      capabilities: ["web", "filesystem"],
      summary: "Captura a página inteira e publica o caminho apenas após o arquivo existir.",
      useWhen: ["Evidência operacional autorizada, diagnóstico ou comprovante visual."],
      avoidWhen: ["A tela contém dado pessoal/sensível sem política de acesso e retenção."],
      properties: [
        ...actionFields,
        ...artifactFields,
        property("screenshotName", "Nome antigo da captura", "Não", "texto", "Existe somente para abrir fluxos criados por versões antigas. Ao salvar, o editor converte esse valor para Nome fixo do arquivo. Em um fluxo novo, deixe este campo vazio e use Nome fixo ou Origem do nome.")
      ],
      example: { id: "capturar-confirmacao", type: "screenshot", name: "Capturar confirmação", fileName: "confirmacao.png", separateByExecution: true, conflictStrategy: "unique", target: "runtime.artefatos.confirmacao" },
      safety: ["Screenshots podem conter dados fiscais, pessoais e credenciais; ficam fora do Git."],
      failures: ["Pasta não autorizada.", "Colisão com estratégia fail.", "Formato diferente de png/jpg/jpeg."]
    }),
    block({
      blockType: "rpa_download_click",
      actionType: "download",
      title: "Download após clique",
      category: "Artefatos",
      capabilities: ["web", "filesystem"],
      summary: "Arma a espera de download antes de clicar e persiste o arquivo recebido.",
      useWhen: ["A interface gera o download por botão ou link."],
      avoidWhen: ["O arquivo deve ser obtido diretamente por URL conhecida; use Download por requisição."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("downloadMode", "Forma de iniciar o download", "Automático", "opção interna", "O editor grava automaticamente click, indicando que o arquivo será obtido ao clicar em um elemento da página. Não altere este valor manualmente; use o bloco Download por requisição quando não houver clique."),
        property("timeoutMs", "Tempo máximo", "Não", "100 a 600000 milissegundos", "É quanto tempo o RPA aguardará o navegador anunciar o arquivo depois do clique. Por exemplo, 30000 significa 30 segundos. Se o prazo terminar, a ação falha sem fingir que houve download.", [], "30000"),
        ...artifactFields
      ],
      example: { id: "baixar-recibo", type: "download", name: "Baixar recibo", downloadMode: "click", selector: "button[data-download='receipt']", separateByExecution: true, conflictStrategy: "unique", target: "runtime.artefatos.recibo" },
      safety: ["A espera é armada antes do clique.", "Sem fileName, preserva o nome sugerido pelo servidor."],
      failures: ["Clique não inicia download.", "Timeout.", "Destino inválido ou colisão."]
    }),
    block({
      blockType: "rpa_download_request",
      actionType: "download",
      title: "Download por requisição",
      category: "Artefatos",
      capabilities: ["web", "filesystem", "http"],
      summary: "Faz GET ou POST com o contexto HTTP autenticado do navegador e salva a resposta.",
      useWhen: ["O endpoint de download é conhecido, autorizado e precisa reutilizar cookies da sessão."],
      avoidWhen: ["O efeito de POST é desconhecido.", "A interface já oferece download por clique."],
      properties: [
        ...actionFields,
        property("downloadMode", "Forma de iniciar o download", "Automático", "opção interna", "O editor grava automaticamente request, indicando que o arquivo será solicitado diretamente ao endereço informado. Não altere este valor manualmente; use o bloco Download por clique quando a tela possuir um botão de download."),
        ...valueFields,
        property("method", "Tipo da chamada ao servidor", "Sim", "opção", "Use GET quando o endereço apenas entrega um arquivo. Use POST somente quando a documentação do sistema exigir dados no pedido e quando o efeito estiver entendido, pois uma chamada POST pode alterar informações no servidor.", ["GET: solicitar leitura", "POST: enviar dados na solicitação"]),
        property("bodyType", "Formato dos dados enviados", "Não", "opção", "Escolha json para enviar uma estrutura JSON, text para enviar texto simples ou form para enviar pares de campos como um formulário. Este campo importa principalmente em POST e deve seguir o contrato da API do sistema.", ["json: estrutura JSON", "text: texto simples", "form: campos de formulário"], "json"),
        property("requestBody", "Dados fixos enviados", "Não", "JSON ou texto", "Use quando o conteúdo enviado é sempre o mesmo. Pode ser um objeto JSON ou texto, conforme o formato escolhido. Não preencha junto com Origem dos dados enviados e nunca grave senhas ou tokens aqui."),
        property("requestBodySource", "Origem dos dados enviados", "Não", "caminho de dados", "Use quando os dados da solicitação mudam por caso. Informe um caminho como runtime.pedidoDownload ou input.filtros. Não preencha junto com Dados fixos enviados."),
        property("requestHeaders", "Cabeçalhos fixos", "Não", "objeto JSON", "Use para cabeçalhos não secretos que são sempre iguais, por exemplo {\"Accept\":\"application/pdf\"}. Não preencha junto com Origem dos cabeçalhos. Credenciais e tokens devem ficar na configuração protegida, nunca no fluxo."),
        property("requestHeadersSource", "Origem dos cabeçalhos", "Não", "caminho de dados", "Use quando os cabeçalhos são montados em tempo de execução. O caminho deve fornecer um objeto, por exemplo runtime.cabecalhosDownload. Não preencha junto com Cabeçalhos fixos."),
        property("timeoutMs", "Tempo máximo", "Não", "100 a 600000 milissegundos", "É o prazo total para o servidor responder e o arquivo ser gravado. Por exemplo, 30000 significa 30 segundos. Se o prazo terminar, o RPA falha sem publicar um caminho de arquivo como se o download tivesse terminado."),
        ...artifactFields
      ],
      example: { id: "baixar-arquivo", type: "download", name: "Baixar arquivo", downloadMode: "request", method: "GET", valueSource: "runtime.urlDownload", bodyType: "json", separateByExecution: true, conflictStrategy: "unique", target: "runtime.artefatos.arquivo" },
      safety: ["POST pode alterar o sistema remoto.", "Tokens e secrets não pertencem aos cabeçalhos do fluxo; resolva-os no host."],
      failures: ["HTTP sem sucesso.", "URL inválida.", "Corpo/cabeçalhos incompatíveis.", "Falha de persistência."]
    }),
    block({
      blockType: "rpa_safe_final",
      actionType: "safeFinalConfirmation",
      title: "Confirmação final segura",
      category: "Segurança",
      capabilities: ["web", "safeFinalConfirmation"],
      summary: "Entrega a etapa final a uma política específica e, quando a comprovação está marcada, só publica sucesso após validar mensagem e protocolo.",
      useWhen: ["Auditar o botão final com proteção específica.", "Comprovar uma conclusão autorizada e devolver feedback estruturado ao worker."],
      avoidWhen: ["Não existe IPagePolicyFactory específica.", "O usuário autorizou somente parar antes do botão.", "A autorização de envio seria controlada apenas pelo JSON ou pela caixa do Blockly."],
      properties: [
        ...actionFields,
        ...locatorFields,
        property("successSelector", "Seletor da mensagem de sucesso", "Com feedback marcado", "CSS", "Identifica o elemento que confirma a conclusão. A política autorizada deve exigir exatamente um elemento visível."),
        property("successText", "Texto esperado na mensagem", "Com feedback marcado", "texto", "Trecho estável que deve existir na mensagem final. Use a confirmação real do sistema, não um texto intermediário como Processando."),
        property("protocolSelector", "Área que contém o protocolo", "Com feedback marcado", "CSS", "Limita o texto usado para extrair o identificador final. O seletor deve identificar exatamente um elemento anexado."),
        property("protocolPattern", "Expressão para extrair o protocolo", "Com feedback marcado", "expressão regular", "Deve encontrar um único valor e possuir o grupo nomeado protocol, por exemplo #(?<protocol>\\d+)."),
        property("completionTarget", "Guardar conclusão em", "Com feedback marcado", "runtime.*", "Destino que recebe true somente depois que resposta, mensagem e protocolo forem comprovados."),
        property("confirmationMessageTarget", "Guardar mensagem em", "Com feedback marcado", "runtime.*", "Destino que recebe o texto completo da mensagem de sucesso comprovada."),
        property("protocolTarget", "Guardar protocolo em", "Com feedback marcado", "runtime.*", "Destino que recebe o valor extraído pelo grupo protocol. Os três destinos devem ser diferentes."),
        property("timeoutMs", "Tempo máximo da comprovação", "Com feedback marcado", "100 a 600000 milissegundos", "Prazo para resposta e sinais finais. O padrão visual do bloco é 60000, equivalente a 60 segundos."),
        ...artifactFields
      ],
      example: { id: "confirmar-operacao", type: "safeFinalConfirmation", name: "Processar confirmação final protegida", selector: "button[data-action='submit']", successSelector: "p.mensagem-sucesso", successText: "Operação concluída", protocolSelector: "body", protocolPattern: "#(?<protocol>\\d+)", completionTarget: "runtime.business.completed", confirmationMessageTarget: "runtime.business.confirmationMessage", protocolTarget: "runtime.business.protocol", timeoutMs: 60000, fileName: "antes-da-confirmacao.png", conflictStrategy: "unique", target: "runtime.artefatos.confirmacaoSegura" },
      safety: ["A caixa de feedback inclui ou omite os sete critérios juntos.", "A caixa e os campos do JSON não autorizam envio.", "No máximo uma instância, sempre como última ação principal.", "É proibida em if, loop e subfluxo.", "A política padrão recusa execução."],
      failures: ["Política ausente.", "Configuração de feedback incompleta.", "Mensagem ou protocolo não comprovados.", "Destinos runtime inválidos ou repetidos.", "Posição inválida.", "Tentativa de efeito sem intertravamento."]
    }),
    block({
      blockType: "rpa_if_value",
      actionType: "if",
      title: "Se valor",
      category: "Controle",
      summary: "Compara valores literais ou vindos do contexto e executa somente um ramo.",
      useWhen: ["Decidir por status, texto, flags, vazio ou expressão regular."],
      avoidWhen: ["Esperar uma mudança futura; a condição avalia o estado atual."],
      properties: [
        ...actionFields,
        property("condition.type", "Tipo interno da condição", "Automático", "opção interna", "O editor preenche value para indicar que esta condição compara dois valores. Não altere manualmente. Para verificar se um elemento existe ou aparece na tela, use o bloco Se elemento em vez deste."),
        property("condition.leftValue", "Primeiro valor fixo", "Um dos dois", "valor JSON", "É o primeiro dado da pergunta. Exemplo: use Ativo para perguntar se Ativo é igual a outro valor. Normalmente o primeiro lado vem de uma variável; preencha este campo ou Origem do primeiro valor, nunca os dois."),
        property("condition.leftSource", "Origem do primeiro valor", "Um dos dois", "caminho de dados", "É onde buscar o primeiro dado da pergunta, por exemplo input.status ou runtime.protocolo. Preencha este campo ou Primeiro valor fixo, nunca os dois."),
        property("condition.operator", "Pergunta que será feita", "Sim", "opção", "Escolhe a comparação: igual, diferente, contém, não contém, começa com, termina com, corresponde a uma expressão regular, está vazio ou não está vazio. As duas opções de vazio não precisam de segundo valor.", ["equals: é igual", "notEquals: é diferente", "contains: contém", "notContains: não contém", "startsWith: começa com", "endsWith: termina com", "matchesRegex: segue o padrão informado", "isEmpty: está vazio", "isNotEmpty: não está vazio"]),
        property("condition.rightValue", "Segundo valor fixo", "Depende da pergunta", "valor JSON", "É o valor usado como comparação, por exemplo Aprovado. É necessário nas perguntas que comparam dois lados e não é usado em está vazio ou não está vazio. Preencha este campo ou Origem do segundo valor, nunca os dois."),
        property("condition.rightSource", "Origem do segundo valor", "Depende da pergunta", "caminho de dados", "Use quando o segundo dado também muda por caso. Informe um caminho como config.statusEsperado. Não é usado nas perguntas de vazio e não pode ser preenchido junto com Segundo valor fixo."),
        property("condition.ignoreCase", "Ignorar maiúsculas e minúsculas", "Não", "sim ou não", "Quando true, textos como APROVADO e aprovado são considerados iguais. Quando false, as letras precisam ter a mesma capitalização. Esta opção afeta comparações de texto, não números ou valores true e false.", ["true: ignorar diferença", "false: considerar diferença"], "false"),
        property("actions", "O que fazer quando a resposta for sim", "Ao menos um dos dois caminhos", "lista de ações", "Conecte em ENTÃO as etapas que devem rodar quando a pergunta for verdadeira. Pode conter um ou vários blocos e também outras condições ou repetições."),
        property("elseActions", "O que fazer quando a resposta for não", "Ao menos um dos dois caminhos", "lista de ações", "Conecte em SENÃO as etapas que devem rodar quando a pergunta for falsa. Se nada precisa acontecer nesse caso, o editor pode deixar este caminho vazio desde que o outro tenha ações.")
      ],
      example: { id: "verificar-status", type: "if", name: "Verificar status", condition: { type: "value", leftSource: "runtime.status", operator: "equals", rightValue: "OK", ignoreCase: true }, actions: [{ id: "marcar-ok", type: "setVariable", name: "Marcar OK", value: true, target: "runtime.ok" }], elseActions: [{ id: "falhar-status", type: "fail", name: "Status inválido", value: "Status inesperado." }] },
      safety: ["Regex deve ser limitada e confiável.", "Ações aninhadas contam no orçamento."],
      failures: ["Operador inválido.", "Lado obrigatório ausente.", "Regex inválida.", "Ambos os ramos vazios."]
    }),
    block({
      blockType: "rpa_if_element",
      actionType: "if",
      title: "Se elemento",
      category: "Controle",
      capabilities: ["web"],
      summary: "Avalia imediatamente o estado atual de um elemento e executa um ramo.",
      useWhen: ["Tratar duas telas possíveis ou estado já estabelecido no DOM."],
      avoidWhen: ["O estado ainda precisa aparecer; use uma espera antes."],
      properties: [
        ...actionFields,
        property("condition.type", "Tipo interno da condição", "Automático", "opção interna", "O editor preenche element para indicar que esta condição verifica o estado de um elemento da página. Não altere manualmente. Para comparar textos, números ou variáveis, use o bloco Se valor."),
        ...locatorFields.map(item => ({ ...item, json: `condition.${item.json}` })),
        property("condition.state", "Estado que será verificado", "Sim", "opção", "Escolha visible para perguntar se aparece na tela; attached para saber se existe no código da página; hidden para saber se está invisível ou ausente; ou detached para exigir que não exista. A verificação é imediata: este bloco não fica esperando o estado mudar.", ["visible: visível", "attached: presente na página", "hidden: oculto ou ausente", "detached: ausente da página"]),
        property("condition.matchMode", "Como tratar vários resultados", "Não", "opção", "Use single em fluxos novos para conferir a quantidade encontrada e revelar seletores ambíguos. first olha apenas o primeiro resultado e existe para compatibilidade com fluxos antigos.", ["single: conferir quantidade", "first: usar o primeiro"], "single em bloco novo"),
        property("actions", "O que fazer quando a resposta for sim", "Ao menos um dos dois caminhos", "lista de ações", "Conecte em ENTÃO as ações que devem rodar quando o elemento estiver no estado escolhido. A verificação acontece uma vez no momento em que o fluxo chega aqui."),
        property("elseActions", "O que fazer quando a resposta for não", "Ao menos um dos dois caminhos", "lista de ações", "Conecte em SENÃO as ações que devem rodar quando o elemento não estiver no estado escolhido. Se você precisa esperar uma mudança, use Aguardar elemento antes ou no lugar desta condição.")
      ],
      example: { id: "decidir-tela", type: "if", name: "Decidir tela", condition: { type: "element", selector: "main[data-page='list']", state: "visible", matchMode: "single" }, actions: [{ id: "marcar-lista", type: "setVariable", name: "Marcar lista", value: true, target: "runtime.listaAberta" }], elseActions: [{ id: "falhar-tela", type: "fail", name: "Tela inesperada", value: "Tela não reconhecida." }] },
      safety: ["A condição não substitui sincronização.", "Use escopo e cardinalidade para não escolher o elemento errado."],
      failures: ["Seletor inválido.", "Mais de um alvo em single.", "Ramos vazios."]
    }),
    block({
      blockType: "rpa_repeat",
      actionType: "repeat",
      title: "Repetir",
      category: "Controle",
      summary: "Repete uma sequência um número controlado de vezes.",
      useWhen: ["Quantidade conhecida literal ou obtida de dados."],
      avoidWhen: ["Percorrer itens de uma lista; use Para cada item."],
      properties: [
        ...actionFields,
        property("times", "Quantidade fixa de repetições", "Um dos dois", "número inteiro de 0 a 1000000", "Use quando a quantidade é sempre a mesma, por exemplo 3. O valor 0 não executa as ações internas. Preencha este campo ou Origem da quantidade, nunca os dois."),
        property("timesSource", "Origem da quantidade", "Um dos dois", "caminho de dados", "Use quando a quantidade muda por caso. Informe um caminho como input.quantidadeParcelas. O valor encontrado precisa ser um número inteiro entre 0 e 1.000.000. Não preencha junto com Quantidade fixa."),
        property("indexVariable", "Nome do contador", "Não", "identificador", "Cria uma variável com a posição atual da repetição, começando em 0. Com o nome tentativa, use loop.tentativa dentro dos blocos internos. Na primeira passagem o valor será 0, na segunda será 1 e assim por diante.", [], "repeatIndex"),
        property("actions", "Etapas que serão repetidas", "Sim", "lista de ações", "Conecte aqui os blocos que devem rodar em cada passagem. Todos eles terminam antes de a próxima repetição começar. Os IDs dessas ações ainda precisam ser exclusivos em todo o fluxo.")
      ],
      example: { id: "tentar-tres-vezes", type: "repeat", name: "Tentar três vezes", times: 3, indexVariable: "tentativa", actions: [{ id: "registrar-tentativa", type: "setVariable", name: "Registrar tentativa", valueSource: "loop.tentativa", target: "runtime.ultimaTentativa" }] },
      safety: ["Cada ação interna consome orçamento em cada volta.", "Cancelamento é verificado a cada iteração."],
      failures: ["Quantidade negativa, não inteira ou acima do limite.", "Corpo vazio."]
    }),
    block({
      blockType: "rpa_for_each",
      actionType: "forEach",
      title: "Para cada item",
      category: "Controle",
      summary: "Percorre uma lista JSON e empilha item e índice em loop.*.",
      useWhen: ["Processar documentos, arquivos, linhas ou qualquer array do caso."],
      avoidWhen: ["A origem não é array.", "Usar aquisição de casos do banco dentro do fluxo."],
      properties: [
        ...actionFields,
        property("items", "Lista fixa", "Um dos dois", "lista JSON", "Use quando os itens são sempre os mesmos e devem ficar gravados no fluxo, por exemplo [\"Matriz\", \"Filial\"]. Preencha este campo ou Origem da lista, nunca os dois."),
        property("itemsSource", "Origem da lista", "Um dos dois", "caminho de dados", "Use quando a lista vem do caso ou de uma etapa anterior, como input.documentos ou loop.documento.arquivos. O valor precisa ser realmente uma lista. Não preencha junto com Lista fixa."),
        property("itemVariable", "Nome dado ao item atual", "Sim", "identificador", "Escolha um nome simples, como documento. Dentro das ações repetidas, use loop.documento para acessar o item atual e loop.documento.nome para acessar uma propriedade dele."),
        property("indexVariable", "Nome do contador", "Não", "identificador", "Cria uma variável com a posição atual, começando em 0. Se o nome for documentoIndex, use loop.documentoIndex dentro das ações. O segundo item terá índice 1.", [], "<nome do item>Index"),
        property("actions", "Etapas executadas para cada item", "Sim", "lista de ações", "Conecte os blocos que devem rodar para cada item da lista. O RPA termina todas as ações do item atual antes de avançar para o próximo.")
      ],
      example: { id: "percorrer-documentos", type: "forEach", name: "Percorrer documentos", itemsSource: "input.documentos", itemVariable: "documento", indexVariable: "documentoIndex", actions: [{ id: "guardar-codigo", type: "setVariable", name: "Guardar código", valueSource: "loop.documento.codigo", target: "runtime.ultimoCodigo" }] },
      safety: ["Loops aninhados preservam os escopos externos.", "Lista vazia executa zero vezes."],
      failures: ["Origem não é lista.", "Variável inválida.", "Mais de um milhão de itens.", "Corpo vazio."]
    }),
    block({
      blockType: "rpa_run_subflow",
      actionType: "runSubflow",
      title: "Executar subfluxo",
      category: "Controle",
      summary: "Executa uma sequência reutilizável definida no mesmo documento.",
      useWhen: ["Reutilizar login, processamento de item ou outra composição técnica."],
      avoidWhen: ["Criar recursão ou esconder uma única ação trivial sem ganho de leitura."],
      properties: [
        ...actionFields,
        property("subflow", "Nome do conjunto reutilizável", "Sim", "identificador", "Informe exatamente o nome de um subfluxo definido no mesmo documento, por exemplo fazer-login. A diferença entre maiúsculas e minúsculas é ignorada, mas usar a mesma escrita facilita a leitura. A execução falha se a definição não existir.")
      ],
      example: { id: "executar-login", type: "runSubflow", name: "Executar login", subflow: "autenticar" },
      safety: ["Compartilha o mesmo runtime do chamador.", "Ciclos e profundidade acima de 32 são rejeitados antes da execução."],
      failures: ["Subfluxo inexistente.", "Ciclo.", "Cadeia profunda demais."]
    }),
    block({
      blockType: "rpa_subflow_definition",
      actionType: "subflows.<nome>",
      title: "Definir subfluxo",
      category: "Controle",
      summary: "Cria uma definição raiz reutilizável; não vira uma ação na sequência principal.",
      useWhen: ["Agrupar uma sequência chamada por Executar subfluxo."],
      avoidWhen: ["Conectar como ação principal; a definição deve permanecer como bloco raiz separado."],
      properties: [
        property("subflows.<nome>", "Nome do conjunto reutilizável", "Sim", "identificador", "É o nome usado pelos blocos Executar subfluxo, por exemplo fazer-login. Deve ser único, começar por letra e conter somente letras, números, ponto, hífen ou sublinhado. Escolha um nome que descreva a tarefa completa."),
        property("subflows.<nome>[]", "Etapas do conjunto", "Sim", "lista de ações", "Conecte uma ou mais ações que formam a tarefa reutilizável. Elas não rodam sozinhas no início do fluxo; serão executadas cada vez que um bloco Executar subfluxo chamar este nome.")
      ],
      example: { subflows: { autenticar: [{ id: "preencher-usuario", type: "fill", name: "Preencher usuário", selector: "input[name='user']", valueSource: "config.usuario" }] } },
      safety: ["Não pode conter safeFinalConfirmation.", "Todos os IDs internos continuam globais."],
      failures: ["Nome duplicado ou inválido.", "Corpo vazio.", "Ciclo entre definições."]
    })
  ];

  const beginnerGuidance = {
    rpa_navigate: {
      plain: "Abre um endereço no navegador, como se a pessoa digitasse a URL e pressionasse Enter. Ele espera apenas o começo da página carregar; se a tela ainda monta campos depois disso, use também um bloco de espera.",
      scenario: "Abrir a tela de login usando a URL recebida em input.portalUrl.",
      steps: ["Lê a URL informada no próprio bloco ou em um caminho de dados.", "Pede ao navegador para abrir esse endereço.", "Espera o HTML inicial da página ficar disponível."],
      success: "O navegador está no endereço esperado. Para comprovar que a tela está pronta, o próximo bloco deve aguardar um campo ou botão característico."
    },
    rpa_click: {
      plain: "Procura um único botão, link ou outro elemento visível e clica nele. Se não encontrar nada ou encontrar duas opções iguais, interrompe o RPA para evitar clicar no lugar errado.",
      scenario: "Clicar no botão “Nova solicitação” depois que a página inicial estiver pronta.",
      steps: ["Monta a busca usando seletor, escopo, texto e iframe, quando informados.", "Confirma que existe exatamente um resultado visível.", "Executa o clique normal do navegador."],
      success: "A mudança esperada pelo clique acontece, por exemplo um formulário aparece. Confirme isso com um bloco Aguardar elemento ou uma leitura."
    },
    rpa_click_optional: {
      plain: "Tenta clicar em algo que pode ou não aparecer. Se o elemento não surgir dentro do tempo definido, o fluxo continua; erros de seletor ou elementos duplicados continuam sendo tratados como falha.",
      scenario: "Fechar um aviso de cookies que aparece somente no primeiro acesso.",
      steps: ["Procura o elemento durante o tempo configurado.", "Se ele aparecer, confirma que é único e clica.", "Se ele apenas não aparecer, segue para o próximo bloco."],
      success: "Quando o aviso existe, ele é fechado. Quando não existe, o RPA continua sem esperar indefinidamente."
    },
    rpa_wait: {
      plain: "Espera uma condição real da página em vez de aguardar um número fixo de segundos. Pode esperar um elemento aparecer, entrar no HTML, desaparecer da tela ou ser removido do HTML.",
      scenario: "Esperar o campo de CNPJ aparecer antes de tentar preenchê-lo.",
      steps: ["Procura o elemento configurado.", "Observa o estado escolhido até ele acontecer ou o tempo acabar.", "Continua quando a condição foi atendida."],
      success: "No momento em que o bloco termina, o elemento está exatamente no estado escolhido. Lembre que “presente no HTML” não significa necessariamente “pronto para clicar”."
    },
    rpa_click_new_page: {
      plain: "Usa este bloco quando um clique abre outra aba ou janela. Além de clicar, ele passa o controle do RPA para a página nova, para que os próximos blocos atuem nela.",
      scenario: "Clicar em “Abrir relatório”, que abre o relatório em outra aba.",
      steps: ["Começa a observar a criação de uma nova página antes do clique.", "Clica no elemento configurado.", "Assume a nova página e espera um elemento que identifique essa tela."],
      success: "Os blocos seguintes passam a enxergar a nova aba, e o elemento informado em readySelector existe nela."
    },
    rpa_switch_page: {
      plain: "Troca o controle para uma aba que já está aberta. O bloco procura a aba pelo endereço ou pelo título e exige encontrar exatamente uma correspondente.",
      scenario: "Voltar para a aba do portal depois de consultar um relatório em outra aba.",
      steps: ["Lista as páginas abertas pelo mesmo navegador.", "Compara URL ou título conforme a configuração.", "Torna a página encontrada a página atual do fluxo."],
      success: "A próxima ação é executada na aba escolhida. Se duas abas correspondem ao mesmo filtro, o bloco falha para não escolher ao acaso."
    },
    rpa_close_page: {
      plain: "Fecha a aba em que o RPA está trabalhando e volta o controle para a última aba que permaneceu aberta. Use somente quando você sabe que existe outra página para continuar.",
      scenario: "Fechar a prévia de um documento e voltar ao formulário principal.",
      steps: ["Confirma que há outra página disponível.", "Fecha a página atual.", "Escolhe a última página restante como página ativa."],
      success: "A aba anterior foi fechada e o próximo bloco consegue localizar elementos na página que permaneceu aberta."
    },
    rpa_wait_stable: {
      plain: "Espera a página parar de mudar antes de continuar. Ele observa chamadas de rede, indicadores de carregamento e alterações nos campos, evitando depender de pausas fixas.",
      scenario: "Depois de anexar uma nota, esperar o portal calcular automaticamente fornecedor e valor.",
      steps: ["Espera as chamadas monitoradas ficarem quietas.", "Confirma que os indicadores visíveis de carregamento sumiram.", "Confirma que os campos permaneceram sem mudança pelo período configurado."],
      success: "A página permaneceu estável pelo intervalo definido. Isso reduz, mas não substitui, a espera por um elemento específico quando o portal oferece um sinal melhor."
    },
    rpa_fill: {
      plain: "Apaga o conteúdo atual de um campo e coloca o valor informado. Use quando o RPA é realmente responsável por definir aquele valor.",
      scenario: "Preencher o usuário na tela de login com config.usuario.",
      steps: ["Lê o valor literal ou o valor indicado por valueSource.", "Encontra exatamente um campo visível.", "Substitui o conteúdo do campo usando o preenchimento padrão do navegador."],
      success: "O campo mostra o valor esperado. Se o site preenche o campo sozinho, prefira Preservar ou preencher para não apagar um valor legítimo."
    },
    rpa_select_option: {
      plain: "Escolhe uma opção em uma lista HTML comum, o elemento select. Não serve para listas personalizadas que abrem uma caixa de pesquisa; para Select2 existe um bloco próprio.",
      scenario: "Selecionar “Energia elétrica” no campo Tipo de serviço.",
      steps: ["Encontra um único select visível.", "Lê o valor desejado.", "Seleciona a opção e confirma que o select ficou com o valor esperado."],
      success: "O valor interno do select corresponde à opção desejada e os eventos normais do campo foram disparados."
    },
    rpa_set_checked: {
      plain: "Marca ou desmarca uma caixa de seleção ou botão de opção. O bloco olha o estado atual e só faz a mudança necessária.",
      scenario: "Marcar “Confiar neste dispositivo por 7 dias” somente se ainda não estiver marcado.",
      steps: ["Encontra exatamente um checkbox ou radio visível.", "Compara o estado atual com true ou false.", "Altera quando necessário e confirma o estado final."],
      success: "A propriedade checked do campo tem o valor pedido. Para controles estilizados, confira se o seletor aponta para o input correto."
    },
    rpa_press_key: {
      plain: "Pressiona uma tecla ou combinação em um elemento, como Enter, Tab, Escape ou Control+A. É útil quando o comportamento do sistema depende do teclado.",
      scenario: "Pressionar Enter no campo de pesquisa depois de informar o número da nota.",
      steps: ["Encontra exatamente um elemento visível.", "Leva a interação para esse elemento.", "Envia a tecla ou combinação escrita no campo key."],
      success: "O efeito esperado da tecla acontece. Use uma espera ou leitura depois para comprovar a mudança, pois pressionar a tecla não garante por si só que o sistema aceitou."
    },
    rpa_type_sequentially: {
      plain: "Digita um caractere por vez, parecido com uma pessoa usando o teclado. Use em campos com máscara ou JavaScript que não aceita um preenchimento instantâneo.",
      scenario: "Digitar uma senha em um componente que rejeita colagem ou preencher um campo mascarado simples.",
      steps: ["Localiza um único campo visível.", "Limpa o valor anterior quando clearFirst está ativo.", "Digita cada caractere com o intervalo escolhido e confere o valor final."],
      success: "O campo contém exatamente o texto enviado. Se o componente divide o código em várias caixas, use Digitar em inputs segmentados."
    },
    rpa_type_across_inputs: {
      plain: "Distribui os caracteres de um código entre várias caixinhas. É o caso comum de OTP ou PIN em que cada dígito possui seu próprio input.",
      scenario: "Distribuir 654321 entre seis campos de autenticação.",
      steps: ["Localiza todos os inputs visíveis que correspondem ao seletor.", "Confirma que a quantidade de campos é igual à quantidade de caracteres.", "Digita um caractere em cada campo e confere o código completo."],
      success: "A junção dos valores de todos os campos forma exatamente o código de entrada. Uma quantidade diferente de campos causa falha em vez de preencher parcialmente."
    },
    rpa_upload: {
      plain: "Anexa um arquivo usando o campo de upload da página. Antes de interagir com o site, confirma que o caminho foi informado e que o arquivo existe.",
      scenario: "Anexar o PDF indicado em attachments.notaPdf.",
      steps: ["Lê e resolve o caminho do arquivo.", "Confirma que o arquivo existe e encontra o input de upload.", "Envia o arquivo e espera a página voltar a ficar estável."],
      success: "O portal recebeu o arquivo e terminou o processamento observável. Quando o portal mostra o nome do anexo, adicione uma espera específica para comprová-lo."
    },
    rpa_preserve_fill: {
      plain: "Protege campos que o próprio site pode preencher. Se o campo estiver vazio, o RPA preenche; se já tiver o mesmo valor, mantém; se tiver outro valor, interrompe para não sobrescrever silenciosamente.",
      scenario: "Após enviar uma nota, confirmar o valor total calculado pelo portal sem apagar o cálculo.",
      steps: ["Lê o valor atual do campo.", "Preenche somente quando ele está vazio.", "Quando já existe valor, compara conforme a regra escolhida e falha se houver divergência."],
      success: "O campo termina com um valor equivalente ao esperado, seja porque já estava correto ou porque foi preenchido pelo RPA."
    },
    rpa_select2: {
      plain: "Seleciona uma opção em um componente Select2, aquela lista personalizada que costuma abrir pesquisa e carregar opções dinamicamente. O bloco interage com a parte visível sem inventar opções no HTML.",
      scenario: "Escolher uma unidade consumidora em uma lista Select2 carregada pelo servidor.",
      steps: ["Verifica se o select interno já possui um valor aceitável.", "Se estiver vazio, abre o controle visível e espera as opções aparecerem.", "Encontra a opção equivalente, clica e confirma o valor interno."],
      success: "O select interno possui o valor esperado e a interface visível mostra a opção escolhida."
    },
    rpa_currency: {
      plain: "Preenche valores monetários em campos com máscara, digitando apenas os algarismos na ordem esperada pelo componente. Antes de alterar, preserva um valor já preenchido quando ele representa o mesmo número.",
      scenario: "Informar 1.234,56 em um campo que formata automaticamente enquanto a pessoa digita.",
      steps: ["Converte o valor esperado para a sequência de algarismos da máscara.", "Preserva um valor atual numericamente igual ou digita quando vazio.", "Sai do campo e compara o número formatado pelo site."],
      success: "O número exibido no campo é numericamente igual ao valor esperado, mesmo que separadores e símbolo monetário tenham outra apresentação."
    },
    rpa_fail: {
      plain: "Interrompe o RPA de propósito e mostra uma mensagem clara. Use quando o fluxo identifica uma situação em que continuar produziria resultado incorreto.",
      scenario: "Parar com “Fornecedor não localizado” quando a pesquisa obrigatória retorna vazia.",
      steps: ["Lê a mensagem fixa ou dinâmica.", "Encerra imediatamente a execução atual.", "Entrega a mensagem ao tratamento de erro do aplicativo ou worker."],
      success: "A execução termina como falha no ponto esperado e a mensagem explica a causa sem expor senha, OTP ou outro dado sensível."
    },
    rpa_transform_path: {
      plain: "Pega um texto que representa um caminho de arquivo e extrai somente uma parte, como nome, extensão ou pasta. Ele não abre, cria nem verifica o arquivo.",
      scenario: "Transformar C:\\Notas\\nota-123.pdf em nota-123.pdf para conferir o nome mostrado pelo portal.",
      steps: ["Lê o caminho de origem.", "Aplica a operação escolhida somente sobre o texto.", "Guarda o resultado em runtime.* para outro bloco usar."],
      success: "O destino em runtime.* contém a parte pedida. A existência do arquivo precisa ser verificada por outra etapa quando isso for necessário."
    },
    rpa_set_variable: {
      plain: "Guarda um valor temporário para ser usado mais adiante no mesmo caso. É parecido com atribuir um valor a uma variável em C#, mas o destino sempre começa por runtime.",
      scenario: "Guardar true em runtime.validacoes.documentoEncontrado depois de uma condição bem-sucedida.",
      steps: ["Lê o valor direto ou busca em outro caminho de dados.", "Mantém o tipo original, inclusive lista ou objeto.", "Grava uma cópia no destino runtime.*."],
      success: "Blocos posteriores conseguem ler o valor pelo caminho informado, sem alterar input.*, config.* ou attachments.*."
    },
    rpa_capture_timestamp: {
      plain: "Guarda a data e a hora atuais em UTC. O uso mais comum é marcar o instante em que o portal pediu um OTP, para ignorar e-mails antigos.",
      scenario: "Salvar runtime.authentication.otpRequestedAt imediatamente antes de clicar em Entrar.",
      steps: ["Consulta o relógio do processo.", "Converte o instante para o formato completo e sem ambiguidade de fuso.", "Grava o texto em runtime.*."],
      success: "O destino contém uma data ISO 8601 completa. O bloco apenas registra o horário; ele não espera nem solicita código."
    },
    rpa_wait_one_time_code: {
      plain: "Pede ao leitor de OTP configurado no worker que procure um código novo. O bloco não conhece senha de e-mail nem Microsoft Graph; ele usa apenas um nome curto que aponta para a configuração protegida.",
      scenario: "Depois que o portal enviou o e-mail, esperar até dois minutos pelo código da caixa configurada como email-otp.",
      steps: ["Lê o horário mínimo salvo por Capturar instante UTC.", "Solicita ao leitor configurado que consulte a caixa nos intervalos definidos.", "Guarda somente o código encontrado no destino temporário runtime.*."],
      success: "O destino contém um código recebido depois do horário mínimo. Se o fluxo não entrar nesse bloco, nenhuma consulta ao e-mail é realizada."
    },
    rpa_read_element: {
      plain: "Lê uma informação de um único elemento da página e guarda o resultado para uso posterior. Pode ler texto, conteúdo de campo, estado marcado ou um atributo HTML.",
      scenario: "Ler o protocolo exibido depois de uma operação e salvar em runtime.protocolo.",
      steps: ["Localiza exatamente um elemento presente no HTML.", "Lê a propriedade escolhida.", "Grava o valor, inclusive verdadeiro/falso ou nulo, em runtime.*."],
      success: "O destino contém o valor observado na página. Use Ler vários elementos quando o seletor deve retornar uma coleção."
    },
    rpa_read_elements: {
      plain: "Lê a mesma informação de vários elementos e guarda uma lista na ordem em que aparecem na página. Uma lista vazia é válida quando não encontrar itens é um resultado esperado.",
      scenario: "Ler os números de todas as notas exibidas em uma tabela de resultados.",
      steps: ["Localiza todos os elementos correspondentes.", "Recusa uma quantidade acima do limite maxItems.", "Lê a propriedade de cada item e grava uma lista JSON em runtime.*."],
      success: "O destino contém uma lista, inclusive uma lista vazia quando nada foi encontrado. A ordem acompanha a página."
    },
    rpa_screenshot: {
      plain: "Tira uma imagem da página inteira e salva no local configurado. O caminho só é disponibilizado ao restante do fluxo depois que o arquivo foi gravado por completo.",
      scenario: "Salvar uma evidência da tela de conferência antes do ponto de envio.",
      steps: ["Escolhe pasta e nome sem permitir que caminho relativo escape da área de saída.", "Captura a página inteira.", "Publica o arquivo e grava o caminho final em runtime.* quando target foi informado."],
      success: "O arquivo existe no caminho final e pode ser registrado pelo worker como artefato. Confira se a imagem não contém dados que deveriam ser protegidos."
    },
    rpa_download_click: {
      plain: "Usa este bloco quando o download começa depois de clicar em um botão ou link. Ele começa a esperar o arquivo antes do clique para não perder um download muito rápido.",
      scenario: "Clicar em “Baixar recibo” e salvar o PDF com um nome controlado.",
      steps: ["Prepara a espera pelo download.", "Localiza um único elemento e clica.", "Recebe o arquivo, resolve conflitos de nome e publica o caminho final."],
      success: "O arquivo foi gravado por completo. Quando target é usado, ele contém o caminho absoluto do arquivo final."
    },
    rpa_download_request: {
      plain: "Baixa um arquivo chamando diretamente um endereço HTTP com a mesma sessão autenticada do navegador. Use somente quando o endereço e o efeito da requisição são conhecidos.",
      scenario: "Baixar um PDF por uma URL fornecida pela própria página, preservando os cookies de login.",
      steps: ["Monta URL, método, cabeçalhos e corpo conforme a configuração.", "Faz a requisição usando a sessão do navegador.", "Valida a resposta e salva o conteúdo como arquivo."],
      success: "A resposta foi bem-sucedida e o arquivo final existe. Em POST, confirme antes que a chamada não cadastra, envia ou altera dados."
    },
    rpa_safe_final: {
      plain: "Serve para testar o último botão de uma operação real sem deixar a confirmação escapar durante uma validação. Ele só é seguro quando o projeto possui uma regra C# específica para reconhecer e bloquear o efeito daquele sistema.",
      scenario: "Abrir a confirmação de “Enviar solicitação”, capturar evidência e cancelar em um ambiente de validação autorizado.",
      steps: ["Ativa a proteção específica do sistema antes do clique.", "Clica e verifica o diálogo ou sinal esperado.", "Captura evidência e cancela; se a proteção não comprovar o bloqueio, falha."],
      success: "A confirmação esperada foi observada, a evidência foi salva e a operação real não foi concluída. Este bloco deve ser único e o último da sequência principal."
    },
    rpa_if_value: {
      plain: "Escolhe entre dois caminhos comparando valores, como um if em C#. Ele pode comparar texto fixo ou dados recebidos e calculados durante o caso.",
      scenario: "Se input.tipoDocumento for “NF”, executar o preenchimento fiscal; caso contrário, seguir pelo ramo alternativo.",
      steps: ["Lê o lado esquerdo e, quando necessário, o lado direito.", "Aplica a comparação escolhida.", "Executa somente os blocos do ramo verdadeiro ou do ramo falso."],
      success: "Somente um ramo foi executado. Coloque uma ação observável em cada caminho durante o teste para confirmar que a condição está correta."
    },
    rpa_if_element: {
      plain: "Escolhe um caminho olhando o estado atual de um elemento da página. Diferente de Aguardar elemento, ele não fica esperando a condição aparecer.",
      scenario: "Se o campo de OTP estiver visível, ler o e-mail e preencher; caso contrário, continuar porque a sessão ainda é confiável.",
      steps: ["Localiza o elemento no estado atual da página.", "Avalia se ele está visível, presente, oculto ou removido.", "Executa apenas o ramo correspondente ao resultado."],
      success: "O ramo compatível com o estado naquele instante foi executado. Se a página ainda estava carregando, use uma espera antes da condição."
    },
    rpa_repeat: {
      plain: "Repete a mesma sequência uma quantidade definida de vezes. O número da volta fica disponível em loop.repeatIndex, começando em zero.",
      scenario: "Executar três verificações iguais em uma tela paginada quando a quantidade é conhecida.",
      steps: ["Lê e valida a quantidade de repetições.", "Para cada volta, atualiza o índice temporário.", "Executa todos os blocos encaixados em DO."],
      success: "A sequência interna executou exatamente a quantidade pedida. Para percorrer uma lista de itens diferentes, use Para cada item."
    },
    rpa_for_each: {
      plain: "Percorre uma lista e executa os blocos internos uma vez para cada item. É equivalente a um foreach em C#.",
      scenario: "Para cada documento em input.documentos, anexar os arquivos pertencentes àquele documento.",
      steps: ["Lê a lista indicada.", "Disponibiliza o item atual e o índice em loop.*.", "Executa os blocos internos e passa ao próximo item."],
      success: "Cada item foi processado uma vez e os nomes temporários deixaram de existir ao sair do loop. Uma lista vazia executa zero vezes."
    },
    rpa_run_subflow: {
      plain: "Chama uma sequência de blocos reutilizável, parecido com chamar um método em C#. O subfluxo usa os mesmos dados temporários da execução principal.",
      scenario: "Chamar o subfluxo autenticar em mais de um ponto do roteiro sem duplicar todos os blocos de login.",
      steps: ["Procura a definição pelo nome.", "Executa os blocos dessa definição na ordem.", "Volta para o bloco seguinte da sequência que fez a chamada."],
      success: "A definição terminou e o fluxo principal continuou. Nomes inexistentes e chamadas circulares são recusados antes da execução."
    },
    rpa_subflow_definition: {
      plain: "Cria o conteúdo reutilizável que será chamado por Executar subfluxo. Pense nele como a definição de um método; ele fica separado da sequência principal e não executa sozinho.",
      scenario: "Definir autenticar com os blocos de usuário, senha, clique e tratamento opcional de OTP.",
      steps: ["Recebe um nome único para a sequência reutilizável.", "Agrupa os blocos encaixados em ACTIONS.", "Fica disponível para chamadas feitas por Executar subfluxo."],
      success: "O subfluxo aparece em subflows no JSON e pode ser chamado pelo nome. Ele deve permanecer como bloco raiz separado no editor."
    }
  };

  window.RpaBlockCatalog = catalog.map(item => ({
    ...item,
    beginner: beginnerGuidance[item.blockType]
  }));
})();
