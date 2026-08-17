# Plano completo de implementação — RpaBlockly V2 Recorder

> Documento de execução para o Codex
> Repositório de referência: <https://github.com/rodrigojager/RpaBlockly>
> Arquitetura-alvo: **RpaBlockly V2**
> Data do plano: 17 de agosto de 2026
> Idioma do produto, mensagens, documentação e testes de aceitação: português do Brasil em UTF-8

---

## 1. Objetivo deste documento

Implementar um **Recorder próprio para o RpaBlockly V2**, distribuído como extensão Chrome Manifest V3, capaz de acompanhar deterministicamente a navegação de uma pessoa em um sistema web e produzir um único arquivo `.rpablockly.zip`.

O cliente poderá:

1. instalar a extensão personalizada;
2. iniciar uma gravação;
3. executar o procedimento normalmente no navegador;
4. conferir as etapas por uma timeline e por um slideshow de screenshots;
5. finalizar a gravação;
6. receber um ZIP;
7. entregar o ZIP ao desenvolvedor.

O desenvolvedor poderá:

1. abrir o editor Blockly da V2;
2. usar **Importar gravação**;
3. inspecionar o roteiro, os elementos, os candidatos de localização, os inputs, os anexos e as evidências;
4. resolver avisos e lacunas;
5. ajustar condições, loops, dados, segredos e regras de negócio;
6. incorporar o conteúdo ao pacote V2;
7. validar e publicar o RPA pelos mecanismos normais da V2.

O Recorder deverá gerar diretamente os documentos nativos da V2. Não haverá Selenium IDE JSON, Playwright script, Puppeteer schema ou outro formato intermediário que exija um conversor externo.

---

## 2. Hierarquia arquitetural e relação com o plano da V2

Este documento **não substitui** o plano-base:

```text
plano-implementacao-rpablockly-resiliencia-seletores.md
```

O plano-base define a arquitetura normativa da V2, especialmente:

- separação entre fluxo, catálogo de localizadores e política;
- referências a localizadores por ID;
- receitas completas de localização;
- `RpaFlow.Packages` e providers de arquivo, SQL e inline;
- snapshots imutáveis por execução;
- resolução centralizada no `LocatorResolver`;
- fallbacks explícitos antes da heurística;
- adaptação determinística das heurísticas do Scrapling;
- aprendizado provisório por execução;
- promoção somente depois de uma execução completa `Succeeded`;
- independência total entre instâncias simultâneas;
- write-back configurável;
- editor V2 com catálogo visual de localizadores.

Este documento acrescenta à V2:

- a extensão `RpaBlockly Recorder`;
- os metadados de autoria necessários para uma gravação;
- o envelope `.rpablockly.zip`;
- evidências visuais;
- captura opcional e criptografada de segredos;
- o importador integrado ao editor V2;
- testes de ponta a ponta Recorder → editor V2 → runtime V2.

Se houver conflito acidental entre este documento e o plano-base, as decisões estruturais do plano-base prevalecem, salvo as extensões explicitamente declaradas aqui, como `origin: recorder`.

### 2.1 O que a versão atual representa

A branch atual do repositório é útil para:

- inventariar os blocos e ações existentes;
- entender os tipos de seletor já suportados;
- reaproveitar regras de validação e segurança;
- mapear capacidades que não podem desaparecer;
- identificar padrões de edição e salvamento que mereçam ser preservados;
- construir fixtures de paridade de expressividade.

Ela **não é** o desenho sobre o qual o Recorder deverá ser acoplado diretamente.

Não planejar a implementação como:

- aumento do `app.js` monolítico atual;
- inclusão de um array de seletores no `FlowDefinition` atual;
- gravação de `selector` diretamente nos blocos atuais;
- extensão do `schemaVersion: 1`;
- dependência permanente de endpoints atuais como único contrato;
- remendo no `FlowLocatorFactory` atual.

A V2 pode ser construída em paralelo ao binário atual. Os três RPAs existentes poderão continuar usando a versão atual até serem reescritos em uma etapa posterior.

### 2.2 Pré-requisitos da V2 para concluir o Recorder

É possível iniciar a extensão, as telas e o pipeline de eventos antes de toda a V2 estar pronta. Entretanto, a integração final exige que estes contratos estejam estabilizados:

- `flow.production.json`, schema 2;
- `locators.production.json`, schema 1;
- `rpa.policy.json`, schema 1;
- JSON Schemas ou contratos equivalentes publicáveis;
- validação cruzada do pacote;
- IDs, receitas e estratégias de localizador;
- catálogo e campo visual de referência no editor;
- API de pacote do editor;
- modelo de inputs e referências a segredos;
- política de importação de anexos e artefatos.

O Codex não deve criar uma segunda definição desses conceitos dentro da extensão.

---

## 3. Decisões obrigatórias de produto

### 3.1 O Recorder é parte da V2

Todo JSON operacional produzido deverá estar no padrão V2. O envelope de gravação pode conter documentos auxiliares, mas os documentos em `package/` são exatamente os documentos que a V2 entende.

### 3.2 Não há compatibilidade de runtime com a versão anterior

O runtime V2 não precisa executar `schemaVersion: 1` do fluxo antigo.

Qualquer forma de seleção que existia anteriormente deverá ser recriável, com trabalho, por uma receita V2, inclusive:

- CSS;
- XPath;
- sintaxe aceita diretamente pelo Playwright;
- frames encadeados;
- scope;
- `hasText` literal ou vindo de dados;
- `scopeHasText` literal ou vindo de dados;
- cardinalidades `single`, `first` e `many`;
- elementos auxiliares de Select2;
- indicadores de prontidão, sucesso e protocolo;
- condições baseadas em elemento;
- upload, download e confirmação final.

`rawPlaywright` continua sendo a válvula de expressividade da V2. O Recorder não precisa gerar essa estratégia por padrão, mas o editor deverá permitir sua utilização posterior.

### 3.3 A captura é determinística

Não usar LLM para:

- interpretar eventos;
- escolher seletores;
- inferir passos;
- consertar gravações;
- reescrever JSON;
- aprovar candidatos.

O mesmo conjunto de eventos, DOMs e configurações deverá produzir a mesma representação normalizada e a mesma ordem de candidatos, desconsiderando campos explicitamente não determinísticos como timestamps e IDs de sessão.

### 3.4 Não há replay para o cliente

A extensão não será um executor de RPA e não deverá conter:

- botão Replay;
- botão Executar;
- engine completa de Playwright, Selenium ou Puppeteer;
- interpretação integral do fluxo V2;
- automação escondida da página.

Ela poderá:

- verificar se um candidato resolve o elemento atual;
- destacar o elemento sob inspeção;
- mostrar a etapa capturada;
- exibir screenshots estáticos;
- voltar a uma etapa da timeline apenas para editar metadados.

Essas ações são ferramentas de autoria, não replay.

### 3.5 Não há companion app no cliente

O MVP deverá funcionar somente com a extensão Chrome. Não exigir:

- executável nativo;
- Native Messaging Host;
- servidor local;
- daemon;
- instalação de .NET, Node.js ou Python no computador do cliente.

Arquivos são produzidos em memória pela extensão e baixados com `chrome.downloads`.

### 3.6 Senhas podem ser capturadas, mas nunca em texto aberto

A captura de senha será:

- desligada por padrão;
- habilitada por sessão com consentimento explícito;
- sinalizada persistentemente na interface durante a gravação;
- criptografada antes de sair da memória transitória;
- mascarada em screenshots, timeline, logs e JSON operacional;
- impossível quando a extensão não possuir uma chave pública destinatária válida.

### 3.7 O cliente recebe um único ZIP

O pacote precisa ser transportável sem instalação adicional. O ZIP deverá conter tudo o que o desenvolvedor precisa para revisar a gravação, respeitados limites e escolhas de privacidade.

### 3.8 Importar não significa converter

O importador do editor V2:

- valida;
- abre;
- compara;
- apresenta;
- mescla ou substitui;
- grava pelos stores da V2.

Ele não traduz um schema externo para o RpaBlockly.

### 3.9 Casos não suportados devem ficar explícitos

Quando não for seguro construir uma ação V2, o Recorder deverá criar uma pendência de autoria, guardar evidência e preservar o evento bruto necessário para análise.

Nunca inventar uma ação possivelmente errada apenas para produzir um fluxo aparentemente completo.

### 3.10 A política de resiliência permanece no nível do RPA

O Recorder cria candidatos e fingerprints. Ele não decide, por elemento, se a execução deve ser estrita ou adaptativa.

Essa escolha continua em `rpa.policy.json`.

---

## 4. Experiência de uso planejada

### 4.1 Instalação

Prever inicialmente dois canais:

1. pacote privado ou instalação unpacked para uso controlado;
2. pacote assinado/distribuído posteriormente, depois de revisão de segurança e política.

A extensão personalizada poderá receber no build:

- nome da organização;
- logotipo;
- identificador do destinatário;
- chave pública para criptografia de segredos;
- limites de tamanho;
- política de captura permitida;
- versão dos contratos V2 aceitos.

Não inserir chave privada no bundle.

### 4.2 Iniciar gravação

O side panel apresenta:

- nome da gravação;
- ambiente ou cliente;
- aviso de privacidade;
- toggle “Permitir capturar senhas nesta sessão”;
- toggle “Incluir cópia dos arquivos enviados”;
- botão **Iniciar gravação**;
- indicador das permissões de site concedidas.

Ao iniciar:

1. criar `recordingSessionId`;
2. congelar as opções da sessão;
3. registrar versão da extensão e do gerador;
4. limpar qualquer estado transitório anterior;
5. instalar/ativar captura apenas nas abas autorizadas;
6. mostrar uma faixa clara de “Gravação ativa”.

### 4.3 Durante a gravação

O painel mostra cada passo em linguagem simples:

```text
1. Abriu https://portal.exemplo/login
2. Preencheu Campo de usuário
3. Preencheu Campo de senha · valor protegido
4. Clicou em Botão Entrar
5. Aguardou a página Área do cliente
6. Selecionou tipo de consulta “Processo”
```

Para cada etapa permitir:

- renomear;
- adicionar comentário;
- excluir;
- marcar como sensível;
- trocar a referência de input sugerida;
- conferir o elemento destacado quando a página ainda estiver compatível;
- abrir o screenshot correspondente.

Não permitir edição arbitrária do JSON no cliente no MVP.

### 4.4 Revisão visual

A função **Revisar gravação** oferece:

- timeline vertical;
- slideshow estático;
- número, nome e tipo da etapa;
- screenshot antes ou depois, conforme política;
- destaque visual do elemento;
- indicação do valor preenchido, mascarado se sensível;
- avisos de aba, frame, popup ou elemento ambíguo;
- comentários inseridos pelo cliente.

Ela nunca navega ou repete ações.

### 4.5 Finalização

Antes de gerar o ZIP:

1. normalizar eventos restantes;
2. validar documentos V2;
3. classificar pendências como bloqueantes ou avisos;
4. gerar thumbnails;
5. criptografar segredos;
6. remover buffers de valores secretos;
7. calcular hashes;
8. criar manifesto;
9. montar o ZIP;
10. iniciar download;
11. limpar o estado sensível da sessão.

O pacote pode ser importável mesmo com pendências bloqueantes. Nesse caso, o editor o trata como **rascunho de autoria** e não permite publicá-lo para execução até a resolução.

---

## 5. Arquitetura final

```text
Chrome / site do cliente
        │
        ▼
RpaBlockly Recorder Extension (MV3)
├── content scripts
├── normalizador de eventos
├── gerador determinístico de ações
├── gerador de candidatos e fingerprints
├── captura e sanitização de screenshots
├── criptografia de segredos
└── empacotador .rpablockly.zip
        │
        ▼
Editor RpaBlockly V2
├── RecorderPackageImportService
├── validação segura do ZIP
├── preview/timeline/slideshow
├── resolução de conflitos e pendências
├── catálogo visual de localizadores
└── aplicação via API/stores da V2
        │
        ▼
Pacote V2 versionado
├── flow.production.json
├── locators.production.json
└── rpa.policy.json
        │
        ▼
IRpaPackageStore
├── FileRpaPackageStore
├── SqlRpaPackageStore
└── InlineRpaPackageSource
        │
        ▼
RpaPackageRuntimeRegistry
        │ snapshot imutável por execução
        ▼
PlaywrightFlowExecutor → LocatorResolver
```

### 5.1 Separação de responsabilidades

| Componente | Responsabilidade | Não deve fazer |
|---|---|---|
| Extensão | Capturar autoria e produzir pacote nativo | Executar RPA completo |
| Importador | Validar, revisar e aplicar pacote | Converter schema externo |
| Editor V2 | Editar fluxo, catálogo e política | Resolver elementos em produção |
| Contracts | Definir e validar documentos | Acessar DOM ou banco |
| Packages | Carregar, versionar e gravar pacote | Conhecer Playwright |
| Playwright | Executar receitas e resiliência | Ler ZIP de gravação |
| Worker | Obter snapshot e orquestrar execução | Sincronizar passos entre instâncias |

---

## 6. Organização recomendada no repositório V2

```text
schemas/
├── flow-v2.schema.json
├── locators-v1.schema.json
├── rpa-policy-v1.schema.json
├── recorder-bundle-v1.schema.json
├── recorder-session-v1.schema.json
├── recorder-evidence-v1.schema.json
└── recorder-issues-v1.schema.json

src/
├── RpaFlow.Contracts/
├── RpaFlow.Runtime/
├── RpaFlow.Packages/
├── RpaFlow.Packages.SqlServer/
├── RpaFlow.Playwright/
├── RpaFlow.Editor/
├── Rpa.Worker/
└── RpaFlow.Recorder.Extension/
    ├── manifest.json
    ├── package.json
    ├── tsconfig.json
    ├── build/
    └── src/
        ├── background/
        ├── content/
        ├── sidepanel/
        ├── capture/
        ├── normalize/
        ├── locators/
        ├── evidence/
        ├── security/
        ├── package/
        └── shared/

tests/
├── RpaFlow.ContractsChecks/
├── RpaFlow.PackagesChecks/
├── RpaFlow.EditorRecorderImportChecks/
├── RpaFlow.PlaywrightChecks/
├── RpaFlow.RecorderContractChecks/
└── recorder-extension-e2e/

tools/
├── RpaFlow.Migrator/
└── scrapling-reference/
```

`RpaFlow.Recorder.Extension` é um projeto TypeScript independente do build .NET, mas usa tipos gerados dos mesmos schemas da V2.

### 6.1 Regra para compartilhamento de contratos

Escolher uma única fonte canônica:

1. JSON Schemas versionados;
2. geração dos DTOs TypeScript usados na extensão;
3. DTOs/serializadores C# verificados contra os mesmos fixtures;
4. validação semântica adicional em C# e TypeScript;
5. testes de contrato cruzado.

Não manter cópias manuais divergentes de enums, nomes de ações ou estratégias.

---

## 7. Contratos V2 que o Recorder deverá produzir

### 7.1 Os três documentos operacionais permanecem separados

O Recorder não colocará o catálogo dentro do fluxo.

```text
package/
├── flow.production.json
├── locators.production.json
└── rpa.policy.json
```

#### `flow.production.json`

Contém lógica, inputs, ações, condições, subfluxos e referências por locator ID.

```json
{
  "schemaVersion": 2,
  "name": "Login e consulta",
  "inputs": [
    {
      "path": "input.recorded.usuario",
      "type": "string",
      "required": true,
      "displayName": "Usuário"
    }
  ],
  "actions": [
    {
      "id": "preencher-usuario",
      "type": "fill",
      "name": "Preencher usuário",
      "target": {
        "locatorId": "login.username",
        "cardinality": "single"
      },
      "valueSource": "input.recorded.usuario"
    }
  ],
  "subflows": {}
}
```

O exemplo é conceitual. Os nomes exatos das propriedades deverão acompanhar o contrato V2 estabilizado, não uma cópia independente criada apenas para a extensão.

#### `locators.production.json`

Contém elementos lógicos, candidatos ordenados, receitas e fingerprints.

```json
{
  "schemaVersion": 1,
  "locators": [
    {
      "id": "login.username",
      "displayName": "Campo de usuário",
      "candidates": [
        {
          "id": "login-username-testid",
          "origin": "recorder",
          "recorderRole": "capturedPrimary",
          "createdAtUtc": "2026-08-17T12:00:00Z",
          "recipe": {
            "target": {
              "strategy": "testId",
              "value": "username"
            }
          },
          "captureMetadata": {
            "unique": true,
            "matchCount": 1,
            "generatorVersion": "1.0.0"
          }
        },
        {
          "id": "login-username-label",
          "origin": "recorder",
          "recorderRole": "capturedAlternative",
          "createdAtUtc": "2026-08-17T12:00:00Z",
          "recipe": {
            "target": {
              "strategy": "label",
              "value": "Usuário",
              "exact": true
            }
          },
          "captureMetadata": {
            "unique": true,
            "matchCount": 1,
            "generatorVersion": "1.0.0"
          }
        }
      ],
      "fingerprints": [
        {
          "tagName": "input",
          "role": "textbox",
          "accessibleName": "Usuário",
          "attributes": {
            "name": "username",
            "autocomplete": "username"
          },
          "classTokens": ["form-control"]
        }
      ]
    }
  ]
}
```

Extensão explícita do plano-base:

- acrescentar `recorder` ao enum de `origin`;
- acrescentar `recorderRole = capturedPrimary | capturedAlternative` somente para candidatos de origem Recorder;
- não preencher `developerRole` em candidatos do Recorder;
- conservar a origem `recorder` depois da aprovação humana;
- registrar aprovação em metadados de autoria ou histórico, sem falsificar a origem;
- usar a posição no array como única prioridade efetiva.

O candidato principal capturado é apenas o melhor candidato determinístico na página de gravação. Ele não recebe a tag “original do desenvolvedor”.

#### `rpa.policy.json`

Contém a política do RPA inteiro.

O Recorder deve gerar uma política conservadora compatível com os enums definitivos da V2. Como padrão:

- modo `strict`, ou o equivalente oficial da V2;
- write-back desabilitado;
- promoção desabilitada;
- heurística desabilitada;
- captura de HTML de falha desabilitada, salvo escolha explícita no editor.

O editor poderá sugerir modo de fallbacks quando todos os candidatos do Recorder tiverem sido verificados como únicos para o mesmo elemento. Não ativar heurística ou aprendizagem silenciosamente.

### 7.2 Receita completa, não apenas string de seletor

Cada candidato precisa preservar o contexto necessário:

```text
frames externos → frames internos → scope → filtros do scope → target → filtros do target
```

Um candidato alternativo pode divergir em qualquer parte da receita. Portanto, não modele os frames uma vez no elemento se isso impedir alternativas com outro caminho.

### 7.3 Identificadores estáveis

Separar:

- ID da ação;
- locator ID lógico;
- ID do candidato;
- ID da evidência;
- ID do evento bruto;
- ID da sessão de gravação.

Regras sugeridas:

- locator ID legível: `login.username`;
- action ID legível: `preencher-usuario`;
- candidate ID determinístico a partir de locator ID, estratégia e hash canônico da receita;
- colisões recebem sufixo estável;
- IDs não dependem da posição da etapa na timeline;
- renomear display name não muda automaticamente locator ID;
- o editor oferece renomeação explícita com atualização atômica de referências.

### 7.4 Status de rascunho

Não adicionar silenciosamente um novo campo ao `flow.production.json` se ele não fizer parte do contrato V2 estabilizado.

O estado de autoria do Recorder deve ficar em:

```text
recording/session.json
recording/issues.json
```

O importador mantém o pacote em uma área de staging até:

- os três documentos passarem na validação estrutural e cruzada;
- todas as pendências bloqueantes serem resolvidas;
- o usuário confirmar a aplicação.

Se a V2 adotar oficialmente um lifecycle no pacote, o Recorder poderá usá-lo. Essa decisão deverá ser feita no contrato V2, não apenas na extensão.

### 7.5 Inputs gravados

Quando o usuário preencher um campo comum:

1. criar ou reutilizar uma declaração de input;
2. fazer a ação apontar para `input.recorded.*`;
3. guardar o valor de exemplo somente em `samples/inputs.sample.json`;
4. permitir que o importador mapeie o valor para `input.*`, `config.*`, `secret.*` ou literal;
5. nunca embutir por padrão o valor capturado na ação.

Campos equivalentes em várias etapas podem reutilizar o mesmo input somente quando houver evidência determinística forte e confirmação visual. Na dúvida, criar caminhos separados para o desenvolvedor consolidar.

### 7.6 Segredos

O documento de fluxo contém apenas referência, por exemplo:

```text
secret.recorded.loginPassword
```

O valor criptografado fica em `secrets/secrets.enc.json`. Na importação, o desenvolvedor escolhe:

- copiar para o secret provider configurado na V2;
- mapear para um segredo existente;
- descartar;
- manter apenas como amostra criptografada fora do pacote operacional.

O runtime nunca deverá depender do arquivo de segredos do Recorder.

---

## 8. Envelope `.rpablockly.zip`

### 8.1 Estrutura canônica proposta

```text
gravacao-cliente.rpablockly.zip
├── manifest.json
├── package/
│   ├── flow.production.json
│   ├── locators.production.json
│   └── rpa.policy.json
├── samples/
│   ├── inputs.sample.json
│   └── uploads/
├── secrets/
│   └── secrets.enc.json
├── evidence/
│   ├── index.json
│   ├── step-0001-after.webp
│   ├── step-0002-after.webp
│   └── thumbnails/
├── recording/
│   ├── session.json
│   ├── events.normalized.json
│   ├── issues.json
│   └── comments.json
└── integrity.json
```

Os arquivos opcionais devem ser omitidos quando vazios. Não criar diretórios decorativos.

### 8.2 `manifest.json`

Campos mínimos:

- `bundleFormat`;
- `bundleVersion`;
- `bundleId`;
- `createdAtUtc`;
- `recorderVersion`;
- `generatorVersion`;
- `rpaPackageRoot`;
- versões dos três schemas V2;
- nome amigável;
- origem `chrome-recorder`;
- chave pública destinatária por ID;
- presença de segredos;
- presença de uploads;
- quantidade de etapas;
- quantidade de pendências bloqueantes e avisos;
- lista de arquivos;
- declaração explícita de que o pacote não contém replay.

### 8.3 `recording/session.json`

Guardar somente metadados úteis à autoria:

- ID e nome da sessão;
- fuso e locale informativos;
- URLs sanitizadas ou padrões de URL;
- opções de captura;
- abas e frames envolvidos;
- versões do navegador e da extensão;
- contador de eventos;
- associação evento → ação → locator → evidência;
- status de finalização;
- avisos de privacidade aceitos.

Não guardar cookies, local storage, session storage, headers de autenticação ou corpos de rede no MVP.

### 8.4 `recording/issues.json`

Cada pendência contém:

- código estável;
- severidade `blocking | warning | info`;
- etapa ou evento relacionado;
- descrição não técnica;
- descrição técnica;
- evidências;
- opções de resolução esperadas no editor;
- indicação se a etapa foi omitida do fluxo operacional.

Exemplos:

- `UNSUPPORTED_CLOSED_SHADOW_ROOT`;
- `AMBIGUOUS_TARGET`;
- `CROSS_ORIGIN_FRAME_NOT_CAPTURED`;
- `POPUP_RELATION_UNCERTAIN`;
- `FILE_NOT_INCLUDED`;
- `SECRET_NOT_CAPTURED`;
- `NAVIGATION_WITH_UNSAFE_QUERY`;
- `CUSTOM_WIDGET_REQUIRES_REVIEW`.

### 8.5 Integridade e proteção contra ZIP malicioso

`integrity.json` lista SHA-256 e tamanho de todos os demais arquivos.

O importador deverá:

- rejeitar caminhos absolutos;
- rejeitar `..`;
- normalizar separadores;
- impedir Zip Slip;
- limitar número de entradas;
- limitar tamanho compactado por entrada;
- limitar tamanho total descompactado;
- limitar razão de compressão;
- rejeitar nomes duplicados sem diferença de caixa;
- validar hashes antes de desserializar conteúdo;
- recusar symlinks e tipos de entrada inesperados;
- não extrair diretamente para o projeto;
- usar staging isolado e descartável;
- limpar staging ao cancelar ou falhar.

### 8.6 Arquivos de upload

Ao capturar `input[type=file]`:

1. registrar a intenção de upload;
2. guardar nome sanitizado, tamanho, MIME declarado e hash quando disponível;
3. criar referência de attachment no fluxo conforme contrato V2;
4. não incluir conteúdo por padrão;
5. permitir inclusão somente com toggle explícito;
6. impor limite por arquivo e total;
7. impedir tipos bloqueados pela política do build;
8. avisar que o arquivo pode conter dados pessoais;
9. guardar a cópia somente em `samples/uploads/`;
10. exigir remapeamento no editor antes da publicação.

---

## 9. Extensão Chrome Manifest V3

### 9.1 Componentes

```text
Service worker
├── ciclo da sessão
├── coordenação de abas e frames
├── checkpoints
├── screenshots
├── criptografia
└── exportação

Content script
├── listeners de eventos
├── inspeção do DOM
├── geração de snapshots sanitizados
├── candidatos de localização
├── fingerprint
└── overlay visual temporário

Side panel
├── controles da gravação
├── timeline
├── slideshow
├── edição leve
├── avisos
└── finalização
```

### 9.2 Permissões

Começar com o menor conjunto possível, por exemplo:

- `activeTab`;
- `scripting`;
- `storage`;
- `downloads`;
- `sidePanel`;
- permissões de host opcionais solicitadas durante o uso.

Usar `tabs` somente se uma necessidade concreta exigir metadados protegidos por essa permissão.

Não solicitar no MVP:

- `debugger`;
- `webRequest` amplo;
- acesso permanente a todos os sites;
- `nativeMessaging`;
- clipboard irrestrito.

Se uma fase futura precisar de `chrome.debugger`, exigir ADR, revisão de segurança e comunicação explícita ao usuário.

### 9.3 Service worker efêmero

Manifest V3 pode suspender o service worker. Portanto:

- não guardar a única cópia da sessão em variáveis globais;
- manter estado não sensível serializável em `chrome.storage.session`;
- gravar checkpoints após cada lote normalizado;
- reconstruir listeners e mapas ao acordar;
- manter segredos somente pelo menor tempo possível;
- criptografar imediatamente antes de persistir qualquer segredo;
- usar IDs idempotentes para não duplicar passos após retomada.

### 9.4 Content scripts e frames

- capturar eventos no início da propagação quando necessário;
- registrar `frameId` e caminho lógico de frames;
- usar `all_frames` apenas com permissões adequadas;
- respeitar isolamento de mundo;
- não injetar bibliotecas remotas;
- não alterar comportamento funcional do site;
- remover overlays imediatamente após screenshot;
- detectar navegação que invalide o DOM inspecionado.

Frames cross-origin podem exigir permissão adicional. Se não houver acesso, criar pendência; não fingir que o seletor foi validado.

### 9.5 Geração do ZIP

Usar biblioteca empacotada localmente, como `fflate` ou equivalente revisada. Não carregar CDN.

A geração deverá:

- ordenar entradas de forma determinística;
- normalizar UTF-8 e finais de linha;
- serializar JSON de forma canônica;
- aplicar limites de memória;
- informar progresso;
- permitir cancelamento antes do download;
- apagar buffers sensíveis no fim, dentro do que JavaScript permite.

---

## 10. Pipeline determinístico de gravação

```text
evento do navegador
   ↓
RawCapturedEvent
   ↓ validação e sanitização
NormalizedUserIntent
   ↓ coalescência e classificação
ação V2 + locator lógico + candidatos + fingerprint
   ↓
evidência visual + metadados de autoria
   ↓
documentos V2 + adjuncts do Recorder
```

### 10.1 Eventos brutos

Capturar somente o necessário:

- click;
- input;
- change;
- submit;
- keydown para teclas semânticas;
- seleção;
- navegação observável;
- abertura/fechamento de aba ou popup;
- escolha de arquivo;
- scroll apenas quando tiver significado de interação, não como ruído;
- eventos de foco apenas como contexto, não como ação por padrão.

Um `RawCapturedEvent` deverá incluir:

- ID;
- timestamp monotônico relativo;
- tab ID lógico;
- frame ID lógico;
- URL sanitizada;
- tipo;
- dados mínimos da interação;
- referência ao DOM snapshot sanitizado;
- referência ao elemento;
- indicador sensível;
- trust/source do evento.

### 10.2 Coalescência

Regras mínimas:

- vários eventos `input` do mesmo campo viram um `fill` com o valor final;
- `input` seguido de `change` não cria duas ações equivalentes;
- click que provoca submit não precisa virar click + submit duplicado;
- checkbox/radio registra o estado final desejado;
- seleção registra o valor/label final conforme ação V2;
- navegações repetidas da mesma transição são deduplicadas;
- eventos gerados pelo próprio overlay são ignorados;
- digitação de tecla Enter só vira `press` quando não estiver representada semanticamente pelo submit;
- pausa/fim força flush do grupo atual.

Os intervalos de coalescência devem ser configurados e cobertos por golden tests.

### 10.3 Mapeamento inicial para ações V2

| Intenção observada | Ação V2 sugerida | Observação |
|---|---|---|
| click em botão/link | `click` | exige alvo único |
| preenchimento textual | `fill` | valor vira input/ref |
| checkbox/radio | `check`/`uncheck` | usar estado final |
| select nativo | `selectOption` | valor e label como evidência |
| tecla semântica | `press` | somente quando necessária |
| upload | ação de upload V2 | arquivo remapeado depois |
| navegação direta | `navigate` | sanitizar URL |
| download provocado | click + expectativa de download | quando observável com segurança |
| popup | ação + relação de nova página | pode exigir revisão |
| widget customizado | composição ou pendência | nunca adivinhar silenciosamente |

O mapeamento deverá usar o catálogo real de ações da V2. Se uma capacidade ainda não existir, registrar pendência em vez de criar um tipo particular do Recorder.

### 10.4 Navegação e SPA

Distinguir:

- navegação explícita digitada/aberta pelo usuário;
- navegação provocada pela ação anterior;
- troca de rota em SPA;
- redirect de autenticação;
- popup/nova aba;
- hash change sem efeito operacional.

Não gerar um `navigate` redundante depois de todo click. Preferir anexar ao passo anterior uma expectativa de página/URL quando o contrato V2 suportar.

### 10.5 Ações irreversíveis

Marcar visualmente ações como:

- enviar;
- pagar;
- protocolar;
- excluir;
- confirmar;
- publicar.

O Recorder não conhece com certeza a semântica de todos os botões. Usar sinais determinísticos e exigir confirmação humana no editor. Nunca habilitar replay ou reexecutar para “testar”.

---

## 11. Gerador determinístico de localizadores

### 11.1 Objetivo

Produzir uma lista ordenada de candidatos que apontem para o mesmo elemento capturado e possam ser compilados pelo `LocatorResolver` V2.

### 11.2 Ordem inicial de estratégias

Usar ranking fixo, calibrável por configuração versionada. Ponto de partida:

1. `testId` explicitamente reconhecido;
2. `role` + accessible name estável;
3. `label` associado;
4. atributos funcionais estáveis, como `name` ou `autocomplete`, em receita suportada;
5. `placeholder` estável;
6. texto visível exato quando semanticamente adequado;
7. CSS por ID estável;
8. CSS composto curto com atributos estáveis;
9. CSS estrutural mínimo;
10. XPath somente quando CSS/locators semânticos não representarem o alvo.

Não gerar `rawPlaywright` automaticamente salvo necessidade comprovada e testada. Essa estratégia continua disponível para edição manual e migração.

### 11.3 Validação de cada candidato

Para entrar no catálogo capturado, o candidato deverá:

- compilar segundo o contrato V2;
- localizar exatamente o elemento-alvo no momento da captura;
- respeitar a cardinalidade esperada;
- registrar `matchCount`;
- ser reavaliado depois da normalização da ação, se o DOM tiver mudado;
- não conter valor secreto;
- não conter token óbvio;
- não depender de atributo rejeitado pela política.

Candidato ambíguo não deve ser rotulado como alternativa válida. Pode aparecer em diagnóstico para o desenvolvedor, mas fora da lista executável.

### 11.4 Atributos instáveis ou sensíveis

Rejeitar ou penalizar fortemente:

- IDs com UUID/nonce/timestamp sem padrão estável;
- classes hash de CSS-in-JS;
- atributos com tokens;
- valores de campo;
- URLs assinadas;
- atributos longos de framework;
- índices `nth-child` profundos;
- árvore inteira desde `html`;
- texto muito longo ou variável;
- dados pessoais presentes no DOM.

Manter listas configuráveis de allowlist, denylist e padrões de instabilidade, todas versionadas e testadas.

### 11.5 Frames, scope e Shadow DOM

- gerar a cadeia de frames de fora para dentro;
- cada frame precisa ter sua própria expressão de localização;
- usar scope somente quando aumentar estabilidade ou desambiguar de forma clara;
- não achatar toda a receita em uma única string;
- open Shadow DOM pode ser representado conforme suporte real do Playwright/V2;
- closed Shadow DOM vira pendência bloqueante.

### 11.6 Fingerprint

Capturar retrato sanitizado compatível com a heurística V2:

- tag;
- tipo de input;
- role;
- accessible name;
- label;
- placeholder;
- atributos estáveis selecionados;
- class tokens estáveis;
- assinatura curta de texto;
- ancestralidade limitada;
- hints de irmãos;
- padrão de URL sanitizado;
- frame/scope contextual.

Não capturar:

- `value` de inputs;
- senha;
- cookies;
- tokens;
- HTML completo;
- dataset indiscriminado;
- query string sensível.

### 11.7 Score de autoria

O score usado pelo Recorder serve apenas para ordenar candidatos previamente validados para o mesmo alvo. Ele não é o score heurístico de relocalização do runtime.

Componentes possíveis:

- semântica da estratégia;
- unicidade;
- comprimento;
- profundidade estrutural;
- quantidade de atributos;
- sinais de instabilidade;
- uso de texto potencialmente variável;
- dependência de posição;
- estabilidade observada em checkpoints da mesma sessão.

Documentar fórmula, pesos e versão. Golden tests deverão falhar quando uma alteração mudar a ordem esperada.

### 11.8 Relação com a heurística Scrapling

O Recorder captura fingerprints compatíveis com o plano-base. Ele **não executa** a relocalização adaptativa para inventar o alvo durante a autoria.

A heurística inspirada no Scrapling pertence ao runtime V2 e somente roda quando `rpa.policy.json` permitir. Continua sujeita a:

- threshold mínimo;
- diferença mínima para o segundo colocado;
- cardinalidade;
- limites de nós e tempo;
- rejeição em score baixo;
- aprendizado provisório;
- commit apenas depois de `Succeeded`.

---

## 12. Screenshots, timeline e slideshow

### 12.1 Estratégia de captura

Para cada passo significativo:

1. pedir ao content script a geometria do elemento;
2. aplicar overlay temporário não interativo;
3. mascarar áreas sensíveis;
4. usar `captureVisibleTab`;
5. remover overlay em `finally`;
6. registrar viewport, device scale factor, scroll e retângulo;
7. gerar thumbnail;
8. associar a evidência à etapa.

Se o elemento estiver fora do viewport, não rolar automaticamente no MVP apenas para obter screenshot, pois isso altera a página. Registrar evidência disponível e aviso.

### 12.2 Aparência

O screenshot deve mostrar:

- contorno contrastante no elemento;
- etiqueta com número da etapa;
- fundo discreto no label;
- máscara sólida em senha e campos sensíveis;
- sem menus ou controles da extensão sobre dados importantes quando evitável.

Atender contraste, zoom, teclado e leitores de tela no side panel.

### 12.3 Antes e depois

Padrão enxuto:

- screenshot depois da ação para a maioria das etapas;
- screenshot antes quando necessário para entender o alvo;
- no máximo duas imagens por etapa;
- limites configuráveis de resolução, qualidade e total do pacote.

### 12.4 Slideshow não é replay

O slideshow renderiza arquivos locais do pacote/sessão. Não:

- carrega a página original;
- envia requests;
- clica;
- preenche;
- executa JavaScript do site;
- tenta autenticar.

### 12.5 Redação e privacidade

Além de senha, permitir ao usuário marcar regiões adicionais para máscara.

O pipeline de screenshots deve realizar a máscara antes de persistir a imagem. Guardar apenas coordenadas de máscara não é suficiente, pois a imagem original continuaria contendo o segredo.

---

## 13. Segurança dos segredos

### 13.1 Criptografia de envelope

Fluxo recomendado:

1. gerar chave AES-256 aleatória por bundle;
2. criptografar cada segredo com AES-256-GCM e nonce único;
3. usar AAD com bundle ID, secret path e versão;
4. cifrar a chave AES com chave pública do desenvolvedor, por RSA-OAEP-SHA-256 ou mecanismo assimétrico aprovado;
5. guardar somente ciphertext, nonce, tag, algoritmo, key ID e wrapped key;
6. limpar referências em memória após empacotar.

Se a V2 padronizar HPKE ou outro envelope moderno, adotar o padrão oficial. Não criar criptografia própria.

### 13.2 Consentimento

Ao habilitar captura de senhas:

- explicar que o valor será incluído criptografado no ZIP;
- mostrar o destinatário/key ID;
- exigir ação explícita por sessão;
- exibir indicador vermelho persistente;
- permitir desligar a qualquer momento;
- ao desligar, não continuar guardando novos valores;
- oferecer exclusão dos segredos já capturados antes da exportação.

### 13.3 Regras de não vazamento

Segredos não podem aparecer em:

- console;
- logs da extensão;
- telemetria;
- `flow.production.json`;
- `locators.production.json`;
- `rpa.policy.json`;
- `inputs.sample.json`;
- `session.json`;
- `events.normalized.json`;
- comentários automáticos;
- screenshots;
- nomes de arquivo;
- mensagens de erro;
- stack traces enviados externamente.

### 13.4 Importação

A chave privada deve permanecer fora do frontend do editor.

Fluxo:

1. backend recebe pacote em staging;
2. valida integridade e algoritmos;
3. identifica key ID;
4. pede acesso ao resolvedor de chave seguro;
5. descriptografa somente quando o desenvolvedor escolher importar/mapear;
6. envia o valor diretamente ao secret provider;
7. não o devolve ao JavaScript em texto claro;
8. apaga temporários e buffers possíveis;
9. registra auditoria sem valor.

---

## 14. Importador integrado ao editor Blockly V2

### 14.1 Princípio

O importador faz parte de `RpaFlow.Editor`, mas opera sobre os serviços e contratos da V2. Ele não será construído como extensão do controlador atual de um único `flow.production.json`.

### 14.2 Serviços sugeridos

```text
RecorderPackageUploadService
RecorderPackageSecurityValidator
RecorderPackageContractValidator
RecorderImportStagingStore
RecorderImportPreviewBuilder
RecorderImportConflictAnalyzer
RecorderImportApplyService
RecorderSecretImportService
RecorderEvidenceService
```

### 14.3 Endpoints sugeridos

```text
POST   /api/recorder-imports/inspect
GET    /api/recorder-imports/{sessionId}
GET    /api/recorder-imports/{sessionId}/evidence/{evidenceId}
POST   /api/recorder-imports/{sessionId}/validate
POST   /api/recorder-imports/{sessionId}/apply
DELETE /api/recorder-imports/{sessionId}
```

Esses endpoints são de staging. A aplicação final usa as APIs/stores normais da V2:

```text
GET/PUT /api/package
GET/PUT /api/flow
GET/PUT /api/locators
GET/PUT /api/policy
```

Os nomes definitivos devem seguir o desenho consolidado da V2.

### 14.4 Fluxo de importação

1. selecionar ZIP;
2. fazer upload com limite;
3. validar estrutura ZIP;
4. validar hashes;
5. validar manifesto;
6. validar os três schemas V2;
7. executar validação cruzada;
8. ler issues e evidências;
9. comparar com pacote V2 aberto;
10. mostrar preview sem alterar projeto;
11. escolher modo de incorporação;
12. resolver conflitos;
13. mapear inputs, segredos e anexos;
14. confirmar;
15. aplicar atomicamente via package store;
16. manter backup/revisão;
17. limpar staging.

Cancelar em qualquer ponto anterior ao apply não altera o pacote de destino.

### 14.5 Modos de incorporação

#### Substituir pacote

Para projeto vazio ou gravação que representa o RPA inteiro.

#### Acrescentar ao fluxo principal

Insere ações em posição escolhida e mescla locators.

#### Importar como subfluxo

Cria subfluxo nomeado, mantendo referências e inputs.

Não implementar merge automático “mágico”. Exibir todas as decisões que possam alterar semântica.

### 14.6 Conflitos

Tratar explicitamente:

- action ID já existente;
- locator ID já existente com mesma semântica;
- locator ID igual com elemento diferente;
- candidate ID duplicado;
- input path duplicado com tipo diferente;
- secret path existente;
- subflow ID existente;
- evidência órfã;
- política diferente;
- versão de schema futura;
- estratégia não suportada.

Opções possíveis:

- reutilizar existente;
- importar com novo ID;
- substituir após confirmação;
- mesclar candidatos do mesmo elemento;
- ignorar item;
- bloquear até revisão.

### 14.7 Tela de preview

Layout recomendado:

```text
┌──────────────────────┬──────────────────────────────┐
│ Timeline             │ Screenshot / detalhes       │
│ 1. Navegar           │ [imagem anotada]            │
│ 2. Preencher usuário │ Elemento: Campo de usuário  │
│ 3. Preencher senha   │ Locator: login.username     │
│ 4. Entrar            │ 2 candidatos verificados    │
├──────────────────────┴──────────────────────────────┤
│ Pendências │ Inputs │ Segredos │ Anexos │ Política │
└─────────────────────────────────────────────────────┘
```

### 14.8 Integração com Blockly

Depois do apply:

- criar blocos a partir das ações V2;
- aplicar auto-layout previsível;
- selecionar a primeira etapa importada;
- mostrar badge “importado do Recorder”;
- preservar comentários e ligação com evidências no histórico de autoria;
- usar `FieldLocatorReference` para cada target/trigger/options/ready/success/protocol;
- nunca exibir somente um ID opaco no bloco.

Exibição compacta sugerida:

```text
🎯 Campo de usuário
principal: data-testid=username · +1 alternativa
```

Mouse, foco, teclado ou toque abre popover:

```text
Campo de usuário
ID: login.username

1. getByTestId("username")
   CAPTURADO PELO RECORDER · PRINCIPAL

2. getByLabel("Usuário")
   CAPTURADO PELO RECORDER · ALTERNATIVA
```

### 14.9 Catálogo de localizadores

O drawer da V2 deverá mostrar:

- display name;
- ID;
- usos no fluxo;
- ordem atual;
- origem;
- papel de origem;
- receita completa;
- frames e scope;
- captura de unicidade;
- fingerprints;
- evidência visual;
- status de revisão humana;
- histórico de promoção posterior;
- ações editar, duplicar, remover, reordenar e tornar principal.

Origem e aprovação são conceitos diferentes:

- `origin = recorder` permanece;
- aprovação humana registra `reviewedBy`, `reviewedAtUtc` ou evento equivalente;
- não converter automaticamente a origem para `developer`.

### 14.10 Evidências após aplicação

Evidências do Recorder são metadados de autoria, não precisam acompanhar obrigatoriamente o pacote mínimo usado em runtime.

Definir store próprio ou área versionada do projeto para:

- screenshots;
- comentários;
- sessão original;
- issues resolvidas;
- relação com ações e locators.

O pacote operacional continua enxuto. A política de retenção deve ser configurável.

---

## 15. Relação com resiliência e aprendizagem da V2

O Recorder não reimplementa as regras do runtime. O fluxo importado passa a usar o mesmo `LocatorResolver` de qualquer RPA V2.

### 15.1 Sequência de resolução

Conforme a política:

```text
override provisório desta execução
        ↓
candidato principal
        ↓
candidatos alternativos ordenados
        ↓
heurística determinística, se habilitada
```

### 15.2 Comportamento dentro da mesma execução

Se o principal e os alternativos falharem e a heurística encontrar um candidato aceito:

- a execução atual guarda override provisório;
- o próximo uso do mesmo locator nessa execução começa pelo candidato recuperado;
- as outras execuções em andamento não recebem esse override;
- não se repete necessariamente toda a sequência em cada item da mesma execução.

### 15.3 Promoção confirmada

Quando uma execução completa termina com `Succeeded`:

- o candidato que efetivamente recuperou o locator pode ir imediatamente para o início;
- o principal que falhou vai para o final;
- tags originais permanecem;
- candidato do Recorder continua `origin: recorder`;
- candidato heurístico continua `origin: heuristic`;
- não exigir três sucessos antes de promover;
- commit usa mutação semântica e compare-and-swap.

Se a execução depois falhar, cancelar, ficar apenas `Validated` ou entrar em retry, descartar aprendizado provisório.

### 15.4 Independência

Nunca implementar:

- lockstep entre instâncias;
- batch lógico que obriga pares a avançar juntos;
- semaphore por RPA envolvendo a execução inteira;
- troca de snapshot no meio da execução;
- espera de uma instância para publicar antes da outra continuar.

Cada execução obtém um snapshot imutável quando começa.

### 15.5 Fonte e write-back

O Recorder não precisa saber se o pacote final será:

- arquivo local;
- SQL;
- inline;
- arquivo + overlay;
- SQL + overlay.

O editor aplica o import pelo `IRpaPackageStore` escolhido. A ordem efetiva dos candidatos fica em `locators.production.json`, mesmo quando o documento estiver armazenado no banco.

Não criar cache TTL obrigatório. Reutilização de snapshot deve ser orientada por revisão/hash.

---

## 16. Observabilidade e notificações

### 16.1 Eventos da importação

Registrar:

- bundle recebido;
- hash;
- validação aprovada/rejeitada;
- quantidade de etapas;
- issues;
- conflitos;
- modo de aplicação;
- IDs remapeados;
- inputs/segredos/anexos mapeados sem valores;
- revisão V2 criada;
- usuário e data;
- staging removido.

### 16.2 Eventos de runtime

O plano-base continua responsável por:

- candidato tentado;
- duração;
- motivo de rejeição;
- fallback utilizado;
- heurística utilizada;
- score e runner-up gap;
- override provisório;
- promoção confirmada;
- conflito de write-back;
- HTML/screenshot sanitizado em falha.

### 16.3 Notificações

Permitir sinks opcionais para:

- importação com pendências;
- falha definitiva;
- fallback usado;
- heurística usada;
- promoção confirmada;
- flapping;
- conflito de persistência.

Falha de notificação nunca transforma uma execução de negócio bem-sucedida em falha.

---

## 17. Fases de implementação

### Fase 0 — confirmar baseline V2 e ADRs

Objetivo: impedir que a equipe implemente o Recorder sobre contratos temporários ou sobre o schema atual.

Entregas:

- leitura integral do plano-base da V2;
- inventário da implementação V2 já existente na branch no momento do trabalho;
- matriz “pronto / em andamento / ausente” dos pré-requisitos;
- ADR do bundle Recorder;
- ADR de captura de segredos;
- ADR de ausência de replay;
- ADR do enum `origin: recorder`;
- ADR de retenção de evidências;
- threat model inicial.

Gate:

- equipe concorda que `flow`, `locators` e `policy` são separados;
- não existe proposta de embutir seletores nos blocos;
- contratos mínimos da V2 têm dono e versão.

### Fase 1 — schemas e fixtures compartilhados

Entregas:

- schemas V2 estabilizados ou importados da base;
- schema do envelope Recorder;
- schema de sessão, evidências e issues;
- `origin: recorder` e `recorderRole` adicionados formalmente;
- tipos TypeScript gerados;
- fixtures válidas e inválidas;
- canonical JSON serializer;
- verificador C# e TypeScript sobre os mesmos golden files.

Gate:

- extensão e backend interpretam exatamente os mesmos documentos;
- propriedade desconhecida é tratada segundo política V2;
- um pacote Recorder mínimo é validado pelos dois ambientes.

### Fase 2 — esqueleto MV3

Entregas:

- manifest;
- build reprodutível;
- CSP restritiva;
- side panel;
- service worker;
- content script;
- permissões opcionais;
- state machine da sessão;
- checkpoints recuperáveis;
- instalação/documentação local.

Gate:

- extensão inicia, pausa, retoma e encerra uma sessão;
- suspensão do service worker não perde estado não sensível;
- não há código remoto;
- não há companion app.

### Fase 3 — captura e normalização

Entregas:

- listeners;
- raw events;
- sanitização;
- coalescência;
- normalized intents;
- suporte inicial a click, fill, check, select, press, navigate e upload;
- modelagem de aba/frame;
- issues para casos não suportados.

Gate:

- mesmos eventos geram mesmos intents;
- digitação vira uma ação;
- duplicidades de submit são removidas;
- unsupported nunca vira ação inventada.

### Fase 4 — gerador de localizadores e fingerprints

Entregas:

- estratégias suportadas pela V2;
- ranking versionado;
- análise de atributos instáveis;
- validação de unicidade;
- IDs estáveis;
- receitas com frames/scope;
- fingerprint sanitizado;
- capture metadata;
- testes em páginas-fixture.

Gate:

- principal e alternativas resolvem o alvo capturado;
- candidato ambíguo não entra na lista executável;
- ordem é determinística;
- nenhum segredo entra no catálogo/fingerprint.

### Fase 5 — geração nativa dos três documentos V2

Entregas:

- mapper intent → ação V2;
- geração de inputs;
- referências a locators;
- catálogo separado;
- política conservadora;
- validação cruzada;
- issues de autoria.

Gate:

- não há `elements` ou `selectors` embutido no fluxo;
- documentos passam no validator oficial da V2;
- nenhuma ação usa seletor bruto diretamente.

### Fase 6 — evidências e revisão visual

Entregas:

- overlay;
- masking;
- screenshots;
- thumbnails;
- evidence index;
- timeline;
- slideshow;
- comentários;
- limites de tamanho.

Gate:

- slideshow não executa página;
- senhas e regiões sensíveis não aparecem;
- evidências continuam associadas após edição leve.

### Fase 7 — segredos e anexos

Entregas:

- consentimento;
- key ID;
- AES-GCM;
- wrapping assimétrico;
- formato `secrets.enc.json`;
- limpeza de estado;
- upload opcional;
- hashes e limites;
- testes de vazamento.

Gate:

- sem chave pública não há captura de senha;
- chave errada não revela conteúdo;
- segredo não aparece em buscas sobre o ZIP descompactado;
- screenshot está mascarado.

### Fase 8 — empacotador e download

Entregas:

- manifesto;
- integridade;
- ordem determinística;
- ZIP;
- progresso;
- download;
- limpeza da sessão;
- fixtures completas.

Gate:

- pacote adulterado falha na validação;
- package root contém os três documentos exatos;
- hashes e tamanhos conferem;
- download funciona sem serviço local.

### Fase 9 — backend seguro de importação

Entregas:

- upload limitado;
- staging;
- defesa Zip Slip/Zip Bomb;
- validação estrutural e semântica;
- preview model;
- leitura controlada de evidências;
- cancelamento e limpeza;
- auditoria.

Gate:

- inspecionar não modifica projeto;
- arquivo malicioso é recusado;
- staging não vaza entre usuários/sessões;
- frontend nunca recebe chave privada.

### Fase 10 — interface de importação V2

Entregas:

- wizard;
- timeline/slideshow;
- issues;
- conflitos;
- mapping de inputs;
- mapping de segredos;
- mapping de anexos;
- modos substituir/acrescentar/subfluxo;
- confirmação.

Gate:

- usuário entende o que será alterado;
- bloqueios impedem apply inseguro;
- cancelamento é idempotente.

### Fase 11 — aplicação no editor e Blockly

Entregas:

- merge semântico;
- remapeamento de IDs;
- gravação atômica dos três documentos;
- revisão/backup;
- geração dos blocos;
- auto-layout;
- `FieldLocatorReference`;
- drawer e popovers;
- preservação de metadados de autoria.

Gate:

- Recorder → ZIP → editor → salvar → reabrir não perde semântica;
- blocos mostram nomes amigáveis;
- catálogo preserva ordem, origem e receitas;
- nenhuma dependência do `app.js` antigo é introduzida como arquitetura.

### Fase 12 — integração com runtime V2

Entregas:

- fixture importada executada em strict;
- fixture importada executada com fallbacks;
- integração com `RpaPackageRuntimeRegistry`;
- observabilidade;
- evidências em falha;
- teste de snapshot.

Gate:

- runtime não conhece o envelope Recorder;
- execução consome somente pacote V2 aplicado;
- cada execução usa snapshot imutável;
- instâncias simultâneas não se esperam.

### Fase 13 — hardening, acessibilidade e release

Entregas:

- threat-model revisto;
- testes de performance e memória;
- testes de acessibilidade;
- política de retenção;
- SBOM/dependências;
- avisos de terceiros;
- pacote reprodutível da extensão;
- manual do cliente;
- manual do desenvolvedor;
- troubleshooting;
- exemplo completo.

Gate:

- outro desenvolvedor consegue instalar, gravar, importar, revisar e executar um exemplo sem a conversa original;
- suíte completa verde;
- nenhuma vulnerabilidade crítica conhecida nas dependências.

---

## 18. Estratégia de pull requests

Evitar um PR gigante. Ordem sugerida:

1. ADRs e matriz de dependências V2.
2. Schemas do Recorder e extensão `origin: recorder`.
3. Esqueleto MV3.
4. Raw events e normalização.
5. Candidatos e fingerprints.
6. Mapper para os três documentos V2.
7. Evidências e slideshow.
8. Segredos e anexos.
9. Bundle e integridade.
10. Backend de staging/importação.
11. Wizard de preview.
12. Merge/aplicação via V2 package stores.
13. Blockly/catalog integration.
14. E2E com runtime.
15. Hardening, documentação e release.

Cada PR deverá:

- compilar isoladamente;
- manter testes anteriores verdes;
- incluir novos testes relevantes;
- não deixar formato temporário sem versionamento;
- atualizar documentação do contrato alterado;
- registrar decisão arquitetural quando houver trade-off de segurança/produto.

---

## 19. Estratégia de testes

### 19.1 Contratos

- bundle mínimo válido;
- três documentos V2 ausentes individualmente;
- versões incompatíveis;
- propriedade desconhecida;
- locator inexistente;
- candidate ID duplicado;
- origem/role incompatíveis;
- receitas incompletas;
- issues órfãs;
- evidências órfãs;
- canonical serialization;
- fixtures compartilhadas C#/TypeScript.

### 19.2 Normalização

- sequência de caracteres → um fill;
- input + change → um passo;
- click + submit → sem duplicidade;
- Enter com e sem submit;
- checkbox alternado várias vezes;
- select;
- SPA route;
- redirect;
- popup;
- pause/resume;
- service worker suspenso;
- evento do overlay ignorado.

### 19.3 Localizadores

- test ID;
- role + name;
- label;
- placeholder;
- texto;
- CSS estável;
- XPath de fallback;
- frame;
- frame encadeado;
- scope;
- open Shadow DOM;
- closed Shadow DOM como issue;
- atributo dinâmico rejeitado;
- candidato ambíguo rejeitado;
- IDs e ordem determinísticos;
- fingerprint sanitizado.

### 19.4 Segurança

- Zip Slip;
- Zip Bomb;
- arquivo duplicado por case;
- hash inválido;
- tamanho excedido;
- MIME falso;
- symlink;
- segredo em campo comum por erro;
- busca do valor secreto em todos os arquivos do ZIP;
- screenshot com máscara;
- nonce repetido recusado;
- ciphertext adulterado;
- key ID desconhecido;
- chave privada ausente do frontend/bundle;
- CSP;
- dependência remota bloqueada.

### 19.5 Editor

- inspect sem alteração;
- cancelamento;
- replace;
- append;
- subflow;
- conflito de action ID;
- conflito de locator ID;
- merge de candidatos equivalentes;
- input type conflict;
- secret mapping;
- upload mapping;
- auto-layout;
- round-trip Blockly ↔ flow;
- popover com teclado/toque;
- evidência associada após remapeamento.

### 19.6 E2E

Cenário obrigatório:

```text
fixture web
  → gravação Chrome
  → .rpablockly.zip
  → inspect no editor V2
  → resolução de pendências
  → apply em FileRpaPackageStore
  → reabrir pacote
  → executar com PlaywrightFlowExecutor
  → conferir resultado e telemetria
```

Repetir ao menos para:

- login simples;
- formulário com select/checkbox;
- iframe;
- upload;
- popup;
- página com seletor principal alterado e fallback funcional;
- página não suportada, confirmando bloqueio seguro.

### 19.7 Determinismo

Executar a mesma fixture várias vezes e comparar:

- ações;
- locator IDs;
- candidate IDs;
- ordem;
- receitas;
- fingerprints;
- issues;
- manifesto sem campos voláteis;
- hashes dos documentos canônicos.

Campos voláteis devem ficar isolados e documentados.

### 19.8 Concorrência da V2

Usar um pacote importado para comprovar:

1. E1 e E2 começam com a mesma revisão;
2. avançam em ritmos diferentes;
3. E2 confirma aprendizagem e publica nova revisão;
4. E1 continua com seu snapshot;
5. E3 começa depois e recebe a nova revisão;
6. nenhuma instância espera outra;
7. conflito é resolvido por IDs/compare-and-swap.

---

## 20. Requisitos não funcionais

### Segurança

- menor privilégio;
- zero segredo em plaintext persistido;
- limites de recursos;
- validação estrita;
- staging isolado;
- nenhuma execução de conteúdo do ZIP;
- nenhuma dependência remota na extensão;
- logs sanitizados;
- revisão de supply chain.

### Desempenho

- listeners leves;
- geração de candidatos sob orçamento;
- screenshots com compressão configurável;
- ZIP sem bloquear a UI por longos períodos;
- progresso em operações demoradas;
- limite total de memória;
- nenhuma varredura integral do DOM a cada tecla.

### Acessibilidade

- side panel navegável por teclado;
- foco visível;
- labels;
- contraste;
- status anunciados;
- slideshow com alt text descritivo;
- popovers acessíveis no editor;
- não depender apenas de cor.

### Privacidade

- consentimento explícito;
- dados mínimos;
- URLs sanitizadas;
- query strings filtradas;
- máscaras antes da persistência;
- retenção configurável;
- exclusão de sessão;
- documentação clara para o cliente.

### Manutenibilidade

- módulos pequenos;
- regras determinísticas puras onde possível;
- schemas versionados;
- código gerado identificado;
- fixtures legíveis;
- telemetria estruturada;
- ADRs;
- nenhuma duplicação de contrato.

---

## 21. Critérios de aceite globais

- [ ] O alvo é a arquitetura RpaBlockly V2, não o editor/runtime atuais.
- [ ] O plano-base de resiliência permanece normativo.
- [ ] A extensão usa Manifest V3.
- [ ] Não há companion app no computador do cliente.
- [ ] Não há LLM.
- [ ] Não há replay funcional para o cliente.
- [ ] A captura é determinística e testada por golden files.
- [ ] O ZIP contém `package/flow.production.json`.
- [ ] O ZIP contém `package/locators.production.json`.
- [ ] O ZIP contém `package/rpa.policy.json`.
- [ ] O fluxo não embute catálogo ou seletores.
- [ ] Ações referenciam locator IDs.
- [ ] Candidatos usam receitas completas.
- [ ] A ordem do array é a prioridade efetiva.
- [ ] `origin: recorder` é distinto de `developer` e `heuristic`.
- [ ] Aprovação humana não apaga a origem Recorder.
- [ ] O pacote é nativo e não exige conversor externo.
- [ ] Casos não suportados geram issues explícitas.
- [ ] O editor importa por staging e preview.
- [ ] Cancelar uma importação não altera o projeto.
- [ ] Apply grava os três documentos atomicamente pela infraestrutura V2.
- [ ] O editor mostra nomes amigáveis e candidatos por popover/drawer.
- [ ] Timeline e slideshow são estáticos.
- [ ] Screenshots são associados às etapas.
- [ ] Senhas só são capturadas com consentimento.
- [ ] Senhas são criptografadas antes de persistir.
- [ ] A chave privada nunca vai para a extensão ou frontend.
- [ ] Segredos não aparecem em JSON operacional, logs ou screenshots.
- [ ] Uploads só são incluídos com consentimento e limites.
- [ ] ZIP malicioso/adulterado é recusado.
- [ ] O pacote operacional aplicado não depende dos adjuncts do Recorder.
- [ ] Runtime consome somente o snapshot V2 validado.
- [ ] Strict falha conforme política.
- [ ] Fallback tenta candidatos ordenados.
- [ ] Heurística permanece opcional e sujeita a threshold/gap.
- [ ] Score baixo não escolhe apenas “o maior disponível”.
- [ ] Aprendizado provisório afeta somente sua execução.
- [ ] Promoção ocorre somente depois de `Succeeded`.
- [ ] Candidato vencedor pode ser promovido imediatamente após sucesso completo.
- [ ] O principal que falhou pode ir ao fim preservando sua tag.
- [ ] Execuções simultâneas são independentes.
- [ ] Não existe sincronização por etapa ou batch lógico.
- [ ] Fonte de pacote pode ser arquivo, SQL ou inline conforme V2.
- [ ] Write-back pode ser Disabled, Memory, Source ou Overlay conforme V2.
- [ ] Não existe cache TTL obrigatório.
- [ ] Todo localizador antigo pode ser reconstruído no modelo novo.
- [ ] Recorder → ZIP → editor → runtime possui teste E2E.
- [ ] Documentação permite uso sem acesso à conversa original.

---

## 22. Riscos e decisões que exigem ADR

- distribuição pública de extensão capaz de capturar senha;
- rotação e revogação de chave pública;
- múltiplos destinatários para o mesmo bundle;
- recuperação de sessão sensível após crash;
- captura de frames cross-origin;
- adoção de `chrome.debugger`;
- captura de rede;
- suporte a closed Shadow DOM;
- gravação de downloads;
- retenção de screenshots/HTML;
- tamanho máximo do ZIP;
- importação multiusuário;
- merge automático de política;
- persistência de evidências no SQL;
- assinatura digital do bundle além de hashes;
- distribuição fora da Chrome Web Store;
- mudanças no contrato V2 necessárias apenas ao Recorder.

Quando houver dúvida, adotar o comportamento mais restritivo e criar issue/ADR, não enfraquecer validação silenciosamente.

---

## 23. Itens fora do escopo

- adaptar os três RPAs reais existentes;
- manter runtime do schema antigo;
- converter Selenium IDE ou Playwright Codegen para V2;
- replay no cliente;
- executor local na extensão;
- companion app;
- LLM;
- captura irrestrita de rede;
- gravação de cookies ou storage;
- bypass de CAPTCHA/MFA;
- suporte garantido a closed Shadow DOM;
- sincronização de instâncias;
- publicação automática sem revisão do desenvolvedor;
- substituir o mecanismo heurístico definido no plano-base.

---

## 24. Referências técnicas para implementação

- Chrome `scripting`: <https://developer.chrome.com/docs/extensions/reference/api/scripting>
- Content scripts: <https://developer.chrome.com/docs/extensions/develop/concepts/content-scripts>
- Chrome `downloads`: <https://developer.chrome.com/docs/extensions/reference/api/downloads>
- `captureVisibleTab`: <https://developer.chrome.com/docs/extensions/reference/api/tabs#method-captureVisibleTab>
- Chrome DevTools Recorder: <https://developer.chrome.com/docs/devtools/recorder/reference>
- Extensões do Chrome Recorder: <https://developer.chrome.com/docs/devtools/recorder/extensions/>
- Selenium IDE: <https://github.com/SeleniumHQ/selenium-ide>
- Playwright Codegen: <https://playwright.dev/docs/codegen>
- Playwright Locators: <https://playwright.dev/docs/locators>
- Playwright Trace Viewer: <https://playwright.dev/docs/trace-viewer>
- `@puppeteer/replay`: <https://github.com/puppeteer/replay>
- Scrapling: <https://github.com/D4Vinci/Scrapling>
- Documentação adaptativa do Scrapling: <https://scrapling.readthedocs.io/en/latest/parsing/adaptive.html>

Esses projetos são referências conceituais. Antes de adaptar código, revisar licença, atribuição e compatibilidade. A implementação final deverá seguir os contratos e a segurança do RpaBlockly V2.

---

## 25. Instrução final para o Codex executor

Ao receber este plano:

1. leia `AGENTS.md`, o README e o plano-base da V2;
2. inspecione o estado real da branch;
3. não presuma que a V2 está totalmente implementada;
4. produza a matriz de pré-requisitos antes de codificar;
5. trate a versão atual apenas como inventário e referência;
6. não modifique o schema antigo para acomodar o Recorder;
7. não embuta catálogo no fluxo;
8. não crie formato intermediário;
9. não implemente replay, companion app ou LLM;
10. trabalhe por marcos e PRs verificáveis;
11. aplique padrões restritivos de segurança;
12. valide o caminho completo Recorder → V2 Editor → V2 Runtime;
13. mantenha todos os documentos, schemas, testes e UI sincronizados;
14. registre divergências arquiteturais em ADR;
15. só declare pronto quando todos os critérios globais estiverem atendidos.

O resultado desejado não é “uma extensão que grava cliques”. É uma **ferramenta de autoria segura, determinística e nativa da RpaBlockly V2**, que reduz drasticamente o trabalho inicial de modelar um RPA sem entregar ao cliente o motor de execução do produto.
