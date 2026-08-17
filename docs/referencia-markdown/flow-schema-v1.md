# Schema do fluxo — versão 1

Este documento descreve o único contrato de fluxo aceito atualmente pelo editor e pelo runtime. A fonte de verdade executável é `RpaFlow.Contracts`: ainda não existe um arquivo JSON Schema separado.

## Relação com Blockly e produção

```text
Blockly ⇄ JSON schema 1 → runtime .NET
  UI        produção       interpretação
```

- O editor precisa importar e exportar todo o contrato usado pelos fluxos.
- Produção executa somente o JSON.
- O workspace Blockly não é parte do schema.
- O loader lê UTF-8 estrito e rejeita propriedades desconhecidas pelo modelo.
- Nomes de propriedades são desserializados sem diferenciar maiúsculas e minúsculas; use `camelCase` nos documentos novos.

## Documento mínimo válido

`actions` precisa conter pelo menos uma ação. Em uma execução normal, o runner ainda cria navegador, contexto e página; este exemplo apenas não navega para uma URL nem interage com um sistema remoto. Com `--validate-only`, nenhum navegador é aberto:

```json
{
  "schemaVersion": 1,
  "name": "Fluxo mínimo",
  "inputs": [],
  "actions": [
    {
      "id": "iniciar-fluxo",
      "type": "setVariable",
      "name": "Registrar início",
      "value": "pronto",
      "target": "runtime.estado"
    }
  ],
  "subflows": {}
}
```

## Estrutura principal

| Propriedade | Tipo | Regra |
| --- | --- | --- |
| `schemaVersion` | número | Obrigatório e igual a `1`. |
| `name` | texto | Obrigatório e não vazio. |
| `inputs` | lista | Requisitos de dados e anexos verificados antes de abrir o navegador. Pode ser vazia. |
| `actions` | lista | Sequência principal, com pelo menos uma ação. |
| `subflows` | objeto | Nome do subfluxo → lista não vazia de ações. Pode ser vazio. |

Toda ação possui:

```json
{
  "id": "identificador-global",
  "type": "tipoTecnico",
  "name": "Descrição operacional"
}
```

- `id`, `type` e `name` são obrigatórios.
- IDs são únicos no documento inteiro, inclusive condições, loops e subfluxos.
- IDs começam por letra ASCII e depois aceitam letras, números, `.`, `_` e `-`.
- `name` serve para leitura, logs e eventos; não escolhe o handler.
- `type` precisa existir no [Catálogo de blocos](catalogo-de-blocos.md).

O modelo compartilhado agrega propriedades de todos os tipos de ação. Informe apenas as propriedades documentadas para o `type` escolhido; uma propriedade conhecida, porém irrelevante, não ganha semântica por estar presente.

## Declaração de requisitos de entrada e anexos

A lista `inputs` permite falhar antes de criar o navegador. Apesar do nome mantido por compatibilidade, cada requisito pode apontar para um dado do caso ou para um anexo já resolvido:

```json
[
  {
    "path": "input.documentos",
    "type": "array",
    "required": true
  },
  {
    "path": "attachments.notaFiscal",
    "type": "string",
    "required": true
  }
]
```

| Propriedade | Regra |
| --- | --- |
| `path` | Obrigatoriamente `input.<caminho>` ou `attachments.<caminho>`, e único na lista. |
| `type` | `any`, `string`, `number`, `boolean`, `object`, `array` ou `null`. |
| `required` | Padrão `true`; quando `false`, a ausência é aceita, mas um valor presente ainda precisa ter o tipo declarado. |

## Contexto de dados

| Prefixo | Conteúdo | Escrita pelo fluxo |
| --- | --- | --- |
| `input.*` | Dados do caso entregues no request. | Não |
| `config.*` | Configuração administrativa e `Blockly.Variables`. | Não |
| `attachments.*` | Caminhos de anexos já resolvidos. | Não |
| `runtime.*` | Valores calculados, capturados e caminhos finais. | Sim |
| `system.*` | `executionId`, `workItemId` e `batchId`. | Não |
| `loop.*` | Item e índice dos loops ativos. | Somente pelo interpretador |

Aliases legados:

- `job.*` resolve em `input.*`;
- `variables.*` resolve em `config.*`.

Novos fluxos devem usar os prefixos canônicos. Por compatibilidade, `config.*` também procura uma chave legada pontuada no objeto de configuração antes de percorrer uma árvore aninhada.

## Gramática dos caminhos

Exemplos válidos:

```text
input.caso.numero
input.documentos[0].arquivos[1].caminho
config.pastaRede
attachments.pdf
loop.documento.arquivos
system.workItemId
runtime.protocolo
```

Regras:

- a raiz precisa ser conhecida;
- cada segmento começa por letra ASCII;
- os demais caracteres podem ser letras, números, `_` ou `-`;
- a leitura aceita índice numérico não negativo entre colchetes;
- a resolução de nomes não diferencia maiúsculas de minúsculas;
- destinos de escrita usam `runtime.<caminho>` e não aceitam índices.

## Valores literais e origens

A maioria das ações aceita exatamente um dos modos:

```json
{
  "value": "texto literal"
}
```

```json
{
  "valueSource": "input.caso.valor"
}
```

Quando o bloco oferece literal JSON, `value` preserva o tipo:

```json
{
  "value": {
    "ativo": true,
    "itens": [1, 2, 3]
  }
}
```

Não informe literal e `*Source` ao mesmo tempo. Ações que precisam de texto simples recusam objeto ou array; `setVariable` e condições conseguem manter valores JSON tipados.

## Instantes e códigos de uso único

`captureTimestamp` registra um marco UTC sem acessar serviços externos:

```json
{
  "id": "marcar-solicitacao-token",
  "type": "captureTimestamp",
  "name": "Marcar o instante anterior ao login",
  "target": "runtime.authentication.otpRequestedAt"
}
```

O valor usa o formato round-trip `O` do .NET. `target` é obrigatório, deve começar com `runtime.` e não aceita índice.

`waitForOneTimeCode` delega a obtenção do token a um provider configurado pelo host:

```json
{
  "id": "aguardar-token",
  "type": "waitForOneTimeCode",
  "name": "Aguardar o código por e-mail",
  "providerAlias": "email-otp",
  "notBeforeSource": "runtime.authentication.otpRequestedAt",
  "target": "runtime.authentication.otp",
  "timeoutMs": 120000,
  "pollIntervalMs": 5000
}
```

| Propriedade | Regra |
| --- | --- |
| `providerAlias` | Obrigatório; começa por letra ASCII e depois aceita letras, números, `.`, `_` e `-`. |
| `notBeforeSource` | Caminho canônico obrigatório cujo valor seja um timestamp round-trip. |
| `target` | Destino obrigatório em `runtime.*`, sem índice. |
| `timeoutMs` | Obrigatório, entre 1.000 e 600.000 ms. |
| `pollIntervalMs` | Obrigatório, entre 500 e 60.000 ms e menor ou igual ao timeout. |

O schema não contém caixa postal, credenciais nem detalhes do Microsoft Graph. O alias é resolvido pelo host por meio de `IOneTimeCodeProvider`. O worker incluído fornece um provider Microsoft Graph configurado fora do fluxo; outros hosts podem fornecer outra implementação. Sem essa dependência, a ação falha somente quando for executada; `captureTimestamp` continua funcionando. A repetição permitida pertence ao polling do provider, não à ação que solicitou o token nem ao login.

## Localizadores e iframes

Ações web e condições de elemento podem usar:

| Propriedade | Função |
| --- | --- |
| `selector` | Seletor CSS do alvo. |
| `scope` | Contêiner opcional que limita a busca. |
| `scopeHasText` | Texto literal do contêiner. |
| `scopeHasTextSource` | Caminho que fornece o texto do contêiner. |
| `hasText` | Texto literal do alvo. |
| `hasTextSource` | Caminho que fornece o texto do alvo. |
| `frameSelectors` | Até 8 seletores de iframe, do externo para o interno. |

Pares literal/origem são exclusivos. Texto de escopo exige `scope`. Uma origem dinâmica nula, vazia ou composta somente por espaços falha em vez de ampliar silenciosamente a busca.

`typeAcrossInputs` é a exceção deliberada à cardinalidade singular. Seu localizador deve encontrar exatamente tantos inputs visíveis quanto os elementos de texto do valor. A ação aceita `delayMs`, `clearFirst` e `blurAfter`, não aceita `matchMode` e verifica a concatenação dos campos depois da digitação. Ela é destinada a OTPs e PINs segmentados; `typeSequentially` continua exigindo que o valor inteiro permaneça em um único campo.

Exemplo com dois iframes:

```json
{
  "id": "abrir-acao-interna",
  "type": "click",
  "name": "Abrir ação do caso",
  "frameSelectors": ["#contentAreaFrame", "iframe"],
  "scope": "tr.caso",
  "scopeHasTextSource": "input.caso.numero",
  "selector": "button[type='button']",
  "hasText": "Abrir"
}
```

Não use `scope` para representar iframe. Cada iframe é outro documento e precisa aparecer em `frameSelectors`.

## Cardinalidade

Esperas e condições de elemento aceitam `matchMode`:

- `single`: recusa mais de um elemento; para `visible` e `attached`, exige um; para `detached`, exige zero; para `hidden`, aceita zero ou um, desde que o elemento único esteja oculto;
- `first`: usa o primeiro resultado para compatibilidade com fluxos antigos.

Quando omitido, o runtime usa `first`. Novos blocos visuais usam `single` como padrão. As demais ações singulares exigem um único alvo quando executadas.

## Condições

Uma ação `if` possui `condition`, `actions` e `elseActions`. Pelo menos um ramo precisa conter uma ação.

### Condição por valor

```json
{
  "id": "validar-status",
  "type": "if",
  "name": "Status está liberado?",
  "condition": {
    "type": "value",
    "leftSource": "input.caso.status",
    "operator": "equals",
    "rightValue": "LIBERADO",
    "ignoreCase": true
  },
  "actions": [
    {
      "id": "guardar-status",
      "type": "setVariable",
      "name": "Guardar status validado",
      "valueSource": "input.caso.status",
      "target": "runtime.status"
    }
  ],
  "elseActions": [
    {
      "id": "falhar-status",
      "type": "fail",
      "name": "Interromper caso não liberado",
      "value": "Caso não está liberado."
    }
  ]
}
```

Operadores:

- `equals` e `notEquals`;
- `contains` e `notContains`;
- `startsWith` e `endsWith`;
- `matchesRegex`;
- `isEmpty` e `isNotEmpty`.

Os dois últimos não possuem lado direito. Os demais exigem exatamente um literal ou source de cada lado. `ignoreCase` tem padrão `false`. Regex possui timeout de 1 segundo.

Para arrays, `contains` e `notContains` comparam itens; `equals` usa igualdade estrutural JSON.

### Condição de elemento

```json
{
  "type": "element",
  "selector": "#mensagem-erro",
  "state": "visible",
  "matchMode": "single"
}
```

Estados: `visible`, `attached`, `hidden` e `detached`. A condição examina o estado atual; ela não espera o estado surgir. Use antes `wait` quando for necessária sincronização.

## Repetições

### `repeat`

Usa exatamente um entre `times` e `timesSource`, possui `actions` não vazio e expõe o índice em `loop.<indexVariable>`:

```json
{
  "id": "tentar-etapas",
  "type": "repeat",
  "name": "Executar três vezes",
  "times": 3,
  "indexVariable": "tentativa",
  "actions": [
    {
      "id": "guardar-tentativa",
      "type": "setVariable",
      "name": "Guardar índice",
      "valueSource": "loop.tentativa",
      "target": "runtime.ultimaTentativa"
    }
  ]
}
```

O índice começa em zero. Quando omitido, `indexVariable` é `repeatIndex`.

### `forEach`

Usa exatamente um entre array literal `items` e `itemsSource`:

```json
{
  "id": "processar-documentos",
  "type": "forEach",
  "name": "Processar documentos",
  "itemsSource": "input.documentos",
  "itemVariable": "documento",
  "indexVariable": "indiceDocumento",
  "actions": [
    {
      "id": "guardar-documento-atual",
      "type": "setVariable",
      "name": "Guardar documento atual",
      "valueSource": "loop.documento",
      "target": "runtime.documentoAtual"
    }
  ]
}
```

Uma lista vazia executa zero iterações. Loops aninhados mantêm os escopos externos acessíveis.

## Subfluxos

Definições ficam no objeto `subflows`:

```json
{
  "subflows": {
    "capturar-evidencia": [
      {
        "id": "screenshot-subfluxo",
        "type": "screenshot",
        "name": "Capturar evidência",
        "fileName": "evidencia.png"
      }
    ]
  }
}
```

A chamada é uma ação normal:

```json
{
  "id": "executar-captura",
  "type": "runSubflow",
  "name": "Executar captura",
  "subflow": "capturar-evidencia"
}
```

Nomes não diferenciam maiúsculas de minúsculas, precisam ser válidos e únicos, e cada definição contém pelo menos uma ação. Referências ausentes, ciclos e profundidade excessiva são recusados.

## Destino de artefatos

Screenshots, downloads e confirmação segura compartilham:

| Propriedade | Semântica |
| --- | --- |
| `destinationDirectory` | Pasta literal relativa ao output ou absoluta. |
| `destinationDirectorySource` | Caminho que fornece a pasta. |
| `fileName` | Nome literal. |
| `fileNameSource` | Caminho que fornece o nome. |
| `separateByExecution` | Cria subpasta por execução; padrão `true`. |
| `conflictStrategy` | `unique`, `fail` ou `overwrite`; padrão `unique`. |
| `target` | Destino opcional `runtime.*` do caminho absoluto concluído. |

Pasta e nome aceitam literal ou source, nunca ambos. Pastas relativas não podem escapar de `Runtime.OutputDirectory`; caminhos absolutos podem ser locais ou UNC.

Para screenshots, um nome sem extensão recebe `.png`. Quando a extensão for informada, ela precisa ser `.png`, `.jpg` ou `.jpeg`, sem diferenciar maiúsculas de minúsculas.

`screenshotName` é uma propriedade legada ainda aceita como fallback literal por `screenshot` e `safeFinalConfirmation`. Um fluxo legado ainda pode utilizá-la durante uma migração. Ao importar e salvar pelo editor, ela é normalizada para `fileName`; use `fileName` em fluxos novos.

## Comprovação da confirmação final

`safeFinalConfirmation` permanece terminal e depende de uma política específica do sistema. O JSON pode declarar como essa política comprova uma conclusão de produção:

```json
{
  "id": "confirmar-operacao",
  "type": "safeFinalConfirmation",
  "name": "Processar confirmação final protegida",
  "selector": "button[type='submit']",
  "successSelector": "p.mensagem-sucesso",
  "successText": "Operação concluída",
  "protocolSelector": "body",
  "protocolPattern": "#(?<protocol>\\d+)",
  "completionTarget": "runtime.business.completed",
  "confirmationMessageTarget": "runtime.business.confirmationMessage",
  "protocolTarget": "runtime.business.protocol",
  "timeoutMs": 60000,
  "fileName": "antes-da-confirmacao.png"
}
```

Os sete campos entre `successSelector` e `protocolTarget` formam um conjunto atômico: todos ausentes preservam fluxos legados usados somente no modo seguro; qualquer campo presente torna todos obrigatórios. Os três destinos devem ser caminhos `runtime.*` distintos. `protocolPattern` deve ser uma expressão regular válida com o grupo nomeado `protocol`.

No Blockly, a caixa **comprovar conclusão e publicar feedback** controla a presença desse conjunto. Ela vem marcada em blocos novos; ao ser desmarcada, os sete campos e o `timeoutMs` de comprovação não são serializados. A caixa é uma conveniência do editor e não cria uma propriedade de autorização no schema.

Esses campos descrevem evidência, não autorização. Somente uma configuração protegida do host pode permitir o efeito irreversível. O modo autorizado precisa validar o conjunto antes do clique e só publicar a conclusão depois de uma resposta bem-sucedida, uma única mensagem visível e um único protocolo extraído.

## Interrupção controlada

`fail` encerra a execução com mensagem literal ou dinâmica, sem produzir efeito externo:

```json
{
  "id": "interromper-login-recusado",
  "type": "fail",
  "name": "Interromper sem repetir login",
  "value": "Autenticação recusada. O login não será repetido."
}
```

Status de negócio, retry e finalização do item continuam sob responsabilidade do worker.

## Limites e invariantes

| Regra | Limite atual |
| --- | --- |
| Profundidade de ações aninhadas | 32 níveis |
| Cadeia de chamadas de subfluxo | 32 níveis |
| Ações estruturais no documento | 1.000.000 |
| Ações executadas por request | 1.000.000 |
| Iterações de um loop | 1.000.000 |
| `frameSelectors` | 8 |
| `timeoutMs` informado | 100 a 600.000 ms |
| `waitForOneTimeCode.timeoutMs` | 1.000 a 600.000 ms |
| `waitForOneTimeCode.pollIntervalMs` | 500 a 60.000 ms e no máximo o timeout |
| `delayMs` | 0 a 1.000 ms |
| `decimalPlaces` | 0 a 6 |
| `readElements.maxItems` | 1 a 10.000; padrão 1.000 |

Outras invariantes:

- `safeFinalConfirmation` pode existir no máximo uma vez;
- precisa ser a última ação da sequência principal;
- não pode ficar em condição, loop ou subfluxo;
- ações compostas e suas ações internas consomem orçamento;
- destinos de escrita só podem usar `runtime.*` sem índices;
- segredos e strings de conexão não pertencem ao fluxo.

## Defaults compatíveis

Propriedades novas foram adicionadas sem alterar JSONs existentes:

| Propriedade omitida | Comportamento do runtime |
| --- | --- |
| `wait.matchMode` | `first` |
| Condição de elemento `matchMode` | `first` |
| `select2.comparison` | comparação legada |
| `typeSequentially.delayMs` | 50 ms |
| `typeSequentially.clearFirst` | `false` |
| `typeSequentially.blurAfter` | `false` |
| `fillMaskedCurrency.decimalPlaces` | 2 |
| `fillMaskedCurrency.delayMs` | 30 ms |
| `fillMaskedCurrency.commitKey` | `Tab` |
| `readElements.maxItems` | 1.000 |
| `separateByExecution` | `true` |
| `conflictStrategy` | `unique` |

O editor pode oferecer defaults mais convenientes para blocos recém-criados, preservando os defaults antigos quando importa um JSON existente.

## Origem local e origem pelo worker

Na execução local, cada projeto adapta seu `appsettings.local.json` para `FlowExecutionRequest`. No processamento em lote, `src/Rpa.Worker` monta o mesmo request para cada item reservado no banco. Essa troca de origem não exige alterar o fluxo quando a forma de `input`, `config` e `attachments` permanece igual.

Aquisição, claim, fila, concorrência, retry e persistência final ficam fora do schema. Veja [Integração do worker com o banco](integracao-worker-banco.md).
