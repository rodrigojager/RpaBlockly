# Arquitetura e interpretação dos fluxos

## Visão geral

A solução separa autoria visual, contrato de produção, interpretação e particularidades de cada portal:

```text
                    AUTORIA
rpa.editor.json → microservidor → Blockly ⇄ JSON schema 1
                                      │
                                      │ salva
                                      ▼
                             flow.production.json
                                      │
                                      │ produção
                                      ▼
appsettings ou worker → FlowExecutionRequest → runtime → handlers → Playwright
                                                        │
                                                        ▼
                                               FlowExecutionResult
```

O editor pode estar fechado durante a execução. O runtime não carrega Blockly, não usa a posição dos blocos e não gera C# a partir do roteiro. Ele interpreta o JSON já validado.

## Camadas

| Projeto | Responsabilidade | Não deve conhecer |
| --- | --- | --- |
| `RpaFlow.Contracts` | Modelo do schema 1, catálogo, loader, métricas e validação. | Playwright, páginas ou configuração de um RPA específico. |
| `RpaFlow.Runtime` | Request/result, contexto de dados, resolução de valores, inputs, limites, falhas e observer. | Seletores, portais ou Blockly. |
| `RpaFlow.Playwright` | Compilação das ações, browser, runner, handlers web, readiness, downloads e screenshots. | Regras de negócio exclusivas de qualquer sistema de destino. |
| `RpaFlow.Editor` | Perfil do RPA, frontend Blockly, conversão bidirecional e gravação segura. | Execução de produção ou credenciais embutidas. |
| Projeto do RPA | Configuração, adaptação dos dados, fluxo, seletores auditados e políticas específicas. | Cópias do interpretador compartilhado. |

## Pipeline de produção

O caminho real de uma execução é:

```text
JsonFlowLoader
  → FlowJsonSerializer
  → FlowDefinitionValidator
  → PlaywrightFlowExecutor
  → FlowCompiler
  → RpaRunner
  → JsonFlowActionStep
  → FlowActionHandlerRegistry
  → handler da categoria
  → Playwright ou operação de dados/arquivo
```

### 1. Carregamento

`JsonFlowLoader` lê bytes em UTF-8 estrito. `FlowJsonSerializer` desserializa sem diferenciar maiúsculas de minúsculas nos nomes das propriedades e rejeita propriedades JSON desconhecidas pelo modelo.

### 2. Validação

`FlowDefinitionValidator` verifica, antes do navegador:

- `schemaVersion` e estrutura principal;
- requisitos declarados em `input.*` e `attachments.*`;
- IDs globais;
- tipos de ação suportados;
- propriedades obrigatórias, mutuamente exclusivas, ranges e enumerações;
- ações aninhadas, referências e ciclos de subfluxos;
- profundidade, quantidade estrutural e posição da confirmação segura.

O modelo de ação atual concentra propriedades de todos os tipos em `FlowActionDefinition`. Por isso, uma propriedade conhecida pelo modelo, mas sem semântica para determinado `type`, pode ser desserializada. Fluxos novos devem informar apenas as propriedades documentadas para aquela ação.

### 3. Compilação

`FlowCompiler` valida novamente o documento e transforma cada ação principal em um `JsonFlowActionStep`. A compilação não gera assembly nem código-fonte; ela cria a sequência de objetos executáveis em memória.

### 4. Preparação do runner

`RpaRunner` valida o `FlowExecutionRequest`, os requisitos de entrada e anexos e as opções do Playwright. Depois, cria navegador, contexto, página, monitor de atividade, readiness, artefatos, orçamento de execução e contexto de dados isolado.

O navegador é criado pelo `BrowserLauncher` a partir de `Runtime.Browser`: os motores e canais clássicos são gerenciados pelo Playwright, enquanto `cloakbrowser` inicia o binário stealth gratuito fixado do CloakBrowser (Chromium 146, sem licença). Nos dois casos, todo o restante do runtime usa somente as interfaces `Microsoft.Playwright`; handlers, fluxos, readiness e políticas não mudam de comportamento por causa do motor escolhido.

Cada chamada recebe seu próprio request e sua própria árvore `runtime`. Nenhum item de trabalho deve depender de variável global com o “caso atual”.

### 5. Dispatch e handlers

Ao executar uma ação, `JsonFlowActionStep`:

1. respeita cancelamento;
2. consome uma unidade do orçamento;
3. consulta o guard antes da ação;
4. publica `actionStarted` ao observer;
5. pede ao registro o handler associado ao `type`;
6. consulta o guard depois que o handler termina;
7. publica conclusão ou falha estruturada.

O checkpoint posterior pode solicitar um encerramento normal da execução. A ação-limite é contabilizada e publicada como concluída, as ações seguintes não são iniciadas e o resultado parcial de `runtime.*` é devolvido normalmente. Essa capacidade pertence ao host; não cria um novo tipo de bloco nem altera o JSON do fluxo.

O registro padrão agrupa os 32 tipos em quatro handlers:

| Handler | Responsabilidade |
| --- | --- |
| `NavigationActionHandler` | Navegação, cliques, esperas, abas e readiness. |
| `FormActionHandler` | Campos, selects, teclas, uploads, Select2 e máscaras. |
| `DataAndArtifactActionHandler` | Dados, instantes, códigos de uso único, leituras, screenshots, downloads, falha controlada e confirmação segura. |
| `ControlFlowActionHandler` | Condições, repetição, `forEach` e subfluxos. |

O registro recusa tipos duplicados e verifica sua sincronização com `FlowActionCatalog` ao ser criado.

### 6. Ações compostas

`if`, `repeat`, `forEach` e `runSubflow` chamam recursivamente o mesmo dispatcher. Ações internas também consomem orçamento e produzem eventos. Loops aninhados empilham escopos para que um loop interno ainda possa acessar nomes do loop externo.

### 7. Resultado

Em sucesso, o executor devolve:

- `ExecutionId`;
- `WorkItemId` e `BatchId`, quando fornecidos;
- cópia de `runtime.*` em `Output`;
- horários UTC de início e fim;
- quantidade de ações executadas.

## Contexto tipado

O request recebe três objetos JSON, clonados antes do uso:

```csharp
new FlowExecutionRequest(
    ExecutionId,
    Input,
    Configuration,
    Attachments,
    WorkItemId,
    BatchId);
```

O contexto expõe:

| Caminho | Origem | Mutável pelo fluxo |
| --- | --- | --- |
| `input.*` | `FlowExecutionRequest.Input` | Não |
| `config.*` | `FlowExecutionRequest.Configuration` | Não |
| `attachments.*` | `FlowExecutionRequest.Attachments` | Não |
| `system.*` | IDs do request | Não |
| `loop.*` | Escopos criados pelo interpretador | Somente pelo interpretador |
| `runtime.*` | Árvore inicialmente vazia | Sim |

`job.*` continua sendo alias de `input.*`, e `variables.*`, de `config.*`, para compatibilidade. O editor deve emitir as raízes canônicas em novos fluxos.

## Localizadores e iframes

A fábrica de localizadores interpreta de forma genérica:

1. `frameSelectors`, do iframe externo para o interno;
2. `scope` opcional;
3. texto literal ou dinâmico do escopo;
4. `selector` do alvo;
5. texto literal ou dinâmico do alvo.

Assim, as cadeias de iframe de um sistema permanecem no JSON e não criam um dispatcher exclusivo para SAP. Uma origem de texto vazia falha para não remover silenciosamente o filtro.

## Toolbox e capabilities

A toolbox é única e completa para todos os RPAs. Um projeto torna-se particular por:

- ordem e configuração de `flow.production.json`;
- dados e anexos;
- seletores do portal;
- opções de runtime;
- política técnica específica, quando necessária.

`FlowActionCatalog` associa ações a metadados como `web`, `filesystem`, `http`, `oneTimeCode` e `safeFinalConfirmation`. No estado atual, esses metadados permitem inspeção e testes, mas o host não executa auditoria prévia nem enforcement automático por capability. Eles não filtram a toolbox, não concedem autorização e não substituem a presença real do handler ou de uma política segura.

Dependências externas continuam sob responsabilidade do host. Para `waitForOneTimeCode`, o executor recebe um `IOneTimeCodeProvider`; o JSON escolhe apenas um alias e limites temporais. O worker desta base registra `MicrosoftGraphEmailOneTimeCodeProvider`, cuja configuração protegida fica em `RpaWorker.EmailReader`. Outros hosts podem injetar SMS ou outro canal sem alterar o schema. O runtime permanece desacoplado de Microsoft Graph, caixa postal e credenciais.

## Atomicidade

Uma ação deve representar uma intenção técnica com limite de falha coerente. “Atômica” não significa obrigatoriamente uma única chamada ao Playwright.

Mantenha junto o que precisa armar uma espera antes da interação ou preservar uma pós-condição, por exemplo:

- clicar e capturar nova aba;
- clicar e capturar download;
- upload seguido da espera interna de readiness;
- confirmação final protegida por política específica.

Para etapas sem essa dependência temporal, prefira composição. `typeSequentially` seguido de `pressKey`, por exemplo, permite explicitar digitação e saída do campo como responsabilidades distintas.

## Falhas e observabilidade

Falhas de execução são classificadas em `FlowExecutionException.Failure`, com:

- categoria;
- indicação técnica `Retryable`;
- IDs da execução, item e lote;
- ação atual;
- URL atual, quando disponível;
- horário.

O observer recebe eventos de execução e ação sem os valores digitados. Uma falha do observer é auxiliar e não deve ocultar o resultado principal. `Retryable` não autoriza repetição infinita nem garante idempotência; o worker decide a política externa.

## Artefatos

Screenshots e downloads usam um contrato comum de destino. O runtime resolve e reserva o nome, grava em temporário quando aplicável e só publica o caminho em `runtime.*` depois do sucesso.

Pastas relativas ficam confinadas a `Runtime.OutputDirectory`. Pastas absolutas podem ser locais ou UNC, mas credenciais de rede permanecem fora do fluxo.

## Worker e banco

O fluxo começa depois que um caso já foi reservado:

```text
worker consulta e reserva → monta request → executa fluxo
→ recebe resultado ou falha → persiste → conclui ou agenda retry
```

Claim, prioridade, fila, concorrência, idempotência e status global não pertencem ao Blockly. Veja [Integração do worker com o banco](integracao-worker-banco.md).

## Segurança operacional

- Não execute ação irreversível sem autorização explícita.
- Não coloque segredos ou strings de conexão no fluxo.
- Não use pausa fixa como readiness.
- Não force clique ou injete estado no DOM para mascarar um problema.
- Não transforme uma ação obrigatória em opcional.
- Não trate storage state ou screenshot como dado inofensivo.
- Preserve um baseline e valide JSON → Blockly → JSON antes de alterar um fluxo em produção.
