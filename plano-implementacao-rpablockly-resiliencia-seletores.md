# Plano de implementação — seletores resilientes no RpaBlockly

Data do plano: 17 de agosto de 2026
Repositório analisado: <https://github.com/rodrigojager/RpaBlockly>
Commit analisado: `bc1d90fc2c839039984e1d08b44006103e3d262b`

## 1. Objetivo

Este documento é um **plano de implementação para modificar de fato o repositório RpaBlockly**, e não apenas uma proposta arquitetural. O Codex deverá alterar contratos, runtime Playwright, worker, persistência, editor Blockly, testes, documentação e templates para que cada elemento de página possa possuir vários métodos de localização ordenados, com fallback automático e uma camada heurística determinística opcional. Quando um candidato alternativo ou heurístico recuperar uma execução, ele poderá tornar-se o candidato prioritário depois que aquela execução completa terminar com sucesso.

A heurística adaptativa não deverá ser inventada do zero. A referência obrigatória será a biblioteca Python [D4Vinci/Scrapling](https://github.com/D4Vinci/Scrapling), em especial seu mecanismo de adaptive relocation. O Codex deverá estudar, testar e **adaptar conscientemente as heurísticas do Scrapling para o modelo .NET/Playwright do RpaBlockly**, mantendo os controles de segurança adicionais definidos neste plano.

O projeto novo não precisa executar diretamente o schema antigo. Entretanto, o novo modelo deve manter **paridade de expressividade**: qualquer forma de localização que o RpaBlockly atual consegue representar precisa poder ser recriada no modelo novo, ainda que a organização do JSON, os IDs e a interface sejam diferentes.

Os três RPAs existentes serão reescritos ou migrados posteriormente. Essa adaptação não faz parte desta implementação inicial.

### 1.1 Referência Python correta

Para evitar ambiguidade durante a implementação: a biblioteca com o mecanismo descrito nas conversas anteriores é o **Scrapling**, pacote Python `scrapling`, repositório `D4Vinci/Scrapling`. O pacote chamado `pyscrappy` é outro projeto e não é a fonte da heurística adaptativa deste plano.

Versão de referência analisada para este documento:

- Scrapling `v0.4.14`;
- commit `5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f`, de 10 de agosto de 2026;
- licença BSD 3-Clause;
- implementação principal em [`scrapling/parser.py`](https://github.com/D4Vinci/Scrapling/blob/5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f/scrapling/parser.py);
- persistência adaptativa em [`scrapling/core/storage.py`](https://github.com/D4Vinci/Scrapling/blob/5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f/scrapling/core/storage.py);
- geração do fingerprint por `_StorageTools` em [`scrapling/core/utils/_utils.py`](https://github.com/D4Vinci/Scrapling/blob/5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f/scrapling/core/utils/_utils.py);
- testes de referência em [`tests/parser/test_adaptive.py`](https://github.com/D4Vinci/Scrapling/blob/5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f/tests/parser/test_adaptive.py).

O commit deve ser mantido explícito no ADR e nos testes de conformidade. Atualizar a referência no futuro deverá ser uma decisão deliberada, com comparação de comportamento, e não uma atualização automática silenciosa.

### 1.2 O que significa “utilizar a biblioteca Python”

Neste plano, significa:

1. usar o código e os testes do Scrapling como especificação externa verificável;
2. executar o Scrapling em um harness Python de desenvolvimento/teste para gerar resultados de referência;
3. adaptar para C# os fingerprints, fatores de similaridade, cálculo de score, varredura e threshold;
4. comparar a implementação .NET com fixtures executadas no Python;
5. documentar divergências intencionais do RpaBlockly, especialmente os controles mais restritivos;
6. preservar avisos e atribuições exigidos pela licença BSD 3-Clause.

O worker de produção **não deverá precisar de Python, de um processo auxiliar nem do pacote Scrapling instalado**. A implementação de produção será nativa em C# para evitar mais um runtime, IPC, deploy e ponto de falha. O Python será uma referência executável nos testes. Caso se decida futuramente chamar o Scrapling em produção, isso exigirá outro ADR, benchmarks e revisão operacional e de segurança.

## 2. Premissas que não podem ser violadas

1. Não haverá compatibilidade de runtime com `schemaVersion: 1`.
2. Haverá um conversor offline para auxiliar a futura adaptação, mas o worker novo não carregará fluxos antigos.
3. Toda localização de elemento do fluxo passará por um resolvedor central.
4. O resolvedor não consultará arquivo nem banco durante uma ação.
5. Cada execução trabalhará com um snapshot imutável obtido quando ela começar.
6. Instâncias simultâneas do mesmo RPA serão completamente independentes.
7. Não haverá barreira, fila serial por RPA, espera por pares ou sincronização de etapas.
8. Uma execução já iniciada nunca trocará seu snapshot por causa de aprendizado produzido por outra execução.
9. Uma execução iniciada depois de uma promoção confirmada poderá receber a revisão nova.
10. Uma falha do seletor principal não encerrará a ação enquanto existirem alternativas permitidas pela política.
11. Heurística nunca aceitará simplesmente o candidato de maior score; haverá threshold e diferença mínima para o segundo colocado.
12. Aprendizado será provisório durante a execução e somente persistido depois de `Succeeded`.
13. `Validated`, `Failed`, `Retry` e `Cancelled` não confirmarão aprendizado.
14. Falha ao persistir aprendizado não poderá transformar uma execução de negócio bem-sucedida em falha nem provocar repetição de efeitos colaterais.
15. Não haverá integração com LLM dentro do executor.
16. Arquivo, SQL e pacote inline serão fontes intercambiáveis do mesmo modelo lógico.
17. A ordem efetiva dos candidatos ficará no próprio catálogo JSON, mesmo quando esse JSON estiver armazenado no SQL.
18. Não haverá cache baseado obrigatoriamente em TTL; a atualização será orientada por revisão/hash.
19. A heurística será uma adaptação rastreável do Scrapling, com testes diferenciais Python versus .NET.
20. Melhorias de segurança do RpaBlockly poderão divergir do Scrapling, mas cada divergência deverá ser explícita, testada e documentada.

## 3. Correção importante sobre o worker atual

O `RpaBackgroundService` atual não busca uma batch completa. Ele chama `ClaimNextAsync` repetidamente até preencher `MaxParallelism` e inicia os itens assim que são reservados.

Portanto, o plano não deve introduzir uma falsa semântica de batch nem fazer execuções do mesmo RPA avançarem juntas.

O comportamento futuro será:

```text
Execução A começa com revisão 10 ──────────────── termina quando puder
Execução B começa com revisão 10 ───── termina antes de A
Execução C começa com revisão 11 ───────────────────── termina depois
```

- A e B são independentes e podem estar em ações completamente diferentes.
- Se B aprender e confirmar um candidato, ela pode criar a revisão 11.
- A continua usando a revisão 10 até terminar.
- C, se começar depois da publicação da revisão 11, usa a revisão 11.
- A não espera B, B não espera A e nenhuma precisa concluir a mesma etapa simultaneamente.

## 4. Arquitetura final desejada

```text
Editor Blockly / API
        │
        ▼
Pacote versionado do RPA
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
        │ fornece snapshot no início de cada execução
        ▼
PlaywrightFlowExecutor
        │
        ▼
LocatorResolver
├── override provisório desta execução
├── candidato principal
├── candidatos alternativos
└── heurística determinística, quando habilitada
        │
        ▼
Resultado independente da execução
├── falhou/cancelou: descarta alterações provisórias
└── succeeded: tenta confirmar e persistir mutações
```

## 5. Glossário

- **Elemento lógico:** entidade com significado para o RPA, como “Botão Entrar”.
- **Locator ID:** identificador estável do elemento lógico, como `login.submit`.
- **Candidato:** uma maneira específica de encontrar o elemento.
- **Receita:** target, frames, scope e filtros necessários para construir um `ILocator`.
- **Principal atual:** primeiro candidato da lista efetiva.
- **Original do desenvolvedor:** candidato originalmente definido como principal; a tag não muda quando ele é rebaixado.
- **Alternativa do desenvolvedor:** candidato adicional criado manualmente.
- **Candidato heurístico:** candidato descoberto pelo motor determinístico.
- **Fingerprint:** descrição estrutural sanitizada do elemento.
- **Snapshot:** fluxo, catálogo e política já carregados e validados, congelados para uma execução.
- **Aprendizado provisório:** ajuste visível apenas para a execução que o descobriu.
- **Aprendizado confirmado:** mutação publicada depois que a execução termina como `Succeeded`.
- **Write-back:** destino da persistência do aprendizado.
- **Revisão:** versão identificável do pacote.

## 6. Novo formato do pacote

### 6.1 `flow.production.json`

O fluxo passa a guardar lógica e referências sem guardar seletores diretamente.

```json
{
  "schemaVersion": 2,
  "name": "Login no portal",
  "inputs": [],
  "actions": [
    {
      "id": "login-submit",
      "type": "click",
      "name": "Entrar no sistema",
      "target": {
        "locatorId": "login.submit",
        "cardinality": "single"
      }
    }
  ],
  "subflows": {}
}
```

### 6.2 `locators.production.json`

```json
{
  "schemaVersion": 1,
  "locators": [
    {
      "id": "login.submit",
      "displayName": "Botão Entrar",
      "candidates": [
        {
          "id": "login-submit-testid",
          "origin": "heuristic",
          "learnedAtUtc": "2026-08-17T12:00:00Z",
          "promotedAtUtc": "2026-08-17T12:01:00Z",
          "recipe": {
            "target": {
              "strategy": "rawPlaywright",
              "selector": "[data-testid='login-submit']"
            }
          }
        },
        {
          "id": "login-submit-name",
          "origin": "developer",
          "developerRole": "alternative",
          "originalOrder": 1,
          "recipe": {
            "target": {
              "strategy": "rawPlaywright",
              "selector": "button[name='entrar']"
            }
          }
        },
        {
          "id": "login-submit-original",
          "origin": "developer",
          "developerRole": "original",
          "originalOrder": 0,
          "recipe": {
            "target": {
              "strategy": "rawPlaywright",
              "selector": "#login > button.primary"
            }
          }
        }
      ],
      "fingerprints": []
    }
  ]
}
```

A posição no array será a ordem efetiva. Não haverá `currentPosition`, pois isso criaria uma segunda fonte de verdade.

### 6.3 Receita completa

Uma alternativa deve conseguir mudar qualquer parte da localização, não apenas a string final.

```json
{
  "recipe": {
    "frames": [
      {
        "strategy": "rawPlaywright",
        "selector": "iframe#externo"
      },
      {
        "strategy": "rawPlaywright",
        "selector": "iframe[name='interno']"
      }
    ],
    "scope": {
      "strategy": "rawPlaywright",
      "selector": "form.login",
      "hasText": {
        "source": "input.nomeEmpresa"
      }
    },
    "target": {
      "strategy": "rawPlaywright",
      "selector": "button[type='submit']",
      "hasText": {
        "literal": "Entrar"
      }
    }
  }
}
```

### 6.4 `rpa.policy.json`

```json
{
  "schemaVersion": 1,
  "locatorResilience": {
    "mode": "adaptive",
    "learningWriteBack": "source",
    "promotion": "afterSuccessfulExecution",
    "failedPrimary": "moveToLast",
    "minimumConfidence": 0,
    "minimumRunnerUpGap": 0
  }
}
```

Os valores definitivos de threshold devem ser calibrados com fixtures e portais reais. Não devem ser escolhidos apenas por intuição.

## 7. Garantia de expressividade do modelo anterior

O runtime novo não carregará o schema antigo, mas a implementação só estará completa quando estes mapeamentos forem comprovados por testes:

| Recurso anterior | Representação nova |
|---|---|
| `Selector` | referência a um locator cuja receita possui `target` |
| `Scope` | `candidate.recipe.scope` |
| `ScopeHasText` | `scope.hasText.literal` |
| `ScopeHasTextSource` | `scope.hasText.source` |
| `HasText` | `target.hasText.literal` |
| `HasTextSource` | `target.hasText.source` |
| `FrameSelectors` | `candidate.recipe.frames[]` |
| `MatchMode = single` | `cardinality = single` |
| `MatchMode = first` | `cardinality = first` |
| `readElements` | `cardinality = many` |
| `typeAcrossInputs` | `cardinality = many` |
| condição de elemento | `condition.locator` |
| `TriggerSelector` | `trigger.locatorId` |
| `OptionSelector` | `options.locatorId` |
| `ReadySelector` | `ready.locatorId` |
| `SuccessSelector` | `success.locatorId` |
| `ProtocolSelector` | `protocol.locatorId` |
| download por clique | `target.locatorId` |
| CSS, XPath ou sintaxe Playwright | `strategy = rawPlaywright` |
| localização opcional | referência normal, com semântica da ação |
| texto vindo de dados | `literal` ou `source` |
| coleção | cardinalidade `many` |

### Estratégias novas

O modelo poderá oferecer:

- `css`;
- `xpath`;
- `role`;
- `label`;
- `placeholder`;
- `text`;
- `testId`;
- `rawPlaywright`;
- `fingerprint`.

`rawPlaywright` será a garantia de paridade: qualquer string anteriormente enviada para `Page.Locator`, `Frame.Locator` ou `FrameLocator` continuará representável sem reinterpretação obrigatória.

## 8. Organização dos projetos

Estrutura proposta:

```text
src/
├── RpaFlow.Contracts
├── RpaFlow.Runtime
├── RpaFlow.Packages
├── RpaFlow.Packages.SqlServer
├── RpaFlow.Playwright
├── RpaFlow.Editor
└── Rpa.Worker
```

### `RpaFlow.Contracts`

- schema do fluxo;
- catálogo de locators;
- política;
- contratos de eventos;
- validações puras;
- serialização estrita.

### `RpaFlow.Packages`

- `RpaPackageSnapshot`;
- `IRpaPackageSource`;
- `IRpaPackageWriter`;
- provider de arquivos;
- revisão e hash;
- escrita atômica;
- merge de mutações;
- registro em memória orientado por revisão.

Não conhecerá Playwright.

### `RpaFlow.Packages.SqlServer`

- provider SQL;
- histórico;
- concorrência otimista;
- importação e exportação.

### `RpaFlow.Playwright`

- compilação das receitas;
- fallback;
- fingerprint;
- heurística;
- aprendizado provisório;
- diagnósticos de localização.

### `Rpa.Worker`

- claim;
- paralelismo livre entre execuções;
- obtenção do snapshot no início de cada item;
- publicação das mutações confirmadas;
- resultados, retry e notificações.

### `RpaFlow.Editor`

- edição do fluxo;
- catálogo visual;
- origem e ordem dos candidatos;
- histórico e restauração.

## 9. Fase 0 — Formalizar decisões

### Objetivo

Registrar as regras antes de alterar os contratos.

### Atividades

Criar ADRs para:

1. schema novo sem runtime legado;
2. fluxo separado do catálogo;
3. candidato como receita completa;
4. snapshot imutável por execução;
5. execuções concorrentes e independentes;
6. ausência de barreira ou serialização por RPA;
7. promoção somente depois de `Succeeded`;
8. write-back configurável;
9. heurística determinística sem LLM;
10. `rawPlaywright` como garantia de expressividade.

Atualizar `AGENTS.md`, que atualmente exige manter o schema 1 sincronizado.

### Critério de conclusão

- matriz de expressividade aprovada;
- significado de sucesso documentado;
- regra de concorrência documentada;
- falha de persistência classificada como warning, não falha de negócio.

## 10. Fase 1 — Criar os contratos novos

### Objetivo

Substituir o modelo atual sem carregar propriedades antigas nas ações.

### Tipos principais

```text
FlowDefinition
FlowActionDefinition
FlowConditionDefinition

LocatorCatalog
LocatorDefinition
LocatorCandidate
LocatorRecipe
LocatorExpression
LocatorTextConstraint
LocatorFingerprint

LocatorUseDefinition
LocatorCardinality

RpaPolicyDefinition
LocatorResiliencePolicy
```

### Regras

- toda referência aponta para ID existente;
- IDs são únicos sem diferença de caixa;
- todo locator possui ao menos um candidato;
- o primeiro candidato é o principal atual;
- existe no máximo um `developerRole = original`;
- `originalOrder` é imutável durante aprendizado;
- `literal` e `source` são excludentes;
- toda receita possui target;
- frames preservam a ordem externo → interno;
- `single`, `first` e `many` continuam possíveis;
- propriedades desconhecidas continuam sendo rejeitadas.

### Critério de conclusão

Um pacote novo consegue representar manualmente todos os itens da matriz de expressividade.

## 11. Fase 2 — Validação cruzada do pacote

### Objetivo

Validar fluxo, catálogo e política em conjunto.

### Validações

- locator referenciado existe;
- função e cardinalidade são compatíveis;
- `click` não usa `many`;
- `readElements` e `typeAcrossInputs` usam `many`;
- opções de Select2 podem usar `many`;
- condições e subfluxos possuem referências válidas;
- não existem ciclos de subfluxo;
- candidatos possuem IDs únicos;
- locators não usados geram warning;
- combinações de source e write-back são válidas;
- política adaptativa possui limites válidos.

### Snapshot

```csharp
public sealed record RpaPackageSnapshot(
    string RpaId,
    PackageRevision Revision,
    string ContentHash,
    FlowDefinition Flow,
    LocatorCatalog Locators,
    RpaPolicyDefinition Policy,
    RpaPackageOrigin Origin);
```

### Critério de conclusão

Nenhuma execução começa com documentos inconsistentes.

## 12. Fase 3 — Provider de arquivos

### Objetivo

Implementar primeiro a fonte mais simples.

### Leitura

- UTF-8 estrito;
- caminhos normalizados;
- proteção contra path traversal;
- leitura conjunta dos documentos;
- validação;
- SHA-256;
- snapshot.

### Escrita

Reaproveitar e generalizar o padrão do `ProjectFileService` atual:

1. lock de escrita por pacote;
2. comparação do hash esperado;
3. mutação em memória;
4. validação;
5. arquivo temporário;
6. leitura e validação do temporário;
7. backup;
8. substituição atômica;
9. nova revisão/hash.

O lock protege apenas a escrita do documento. Ele não sincroniza execuções de RPA nem suas etapas.

### Critério de conclusão

Concorrência de escrita é detectada e nenhum JSON válido é silenciosamente sobrescrito.

## 13. Fase 4 — Runtime estrito no modelo novo

### Objetivo

Executar o schema novo sem fallback e sem heurística, comprovando paridade.

### Nova interface

```csharp
Task<ResolvedLocator> ResolveAsync(
    LocatorResolutionRequest request,
    CancellationToken cancellationToken);
```

O request conterá:

- locator ID;
- função: target, trigger, options, ready, success ou protocol;
- cardinalidade;
- estado esperado;
- timeout total;
- dados da execução;
- página atual;
- catálogo do snapshot;
- política do RPA.

### Compilação da receita

```text
frames
  → scope
    → scope.hasText
      → target
        → target.hasText
```

### Centralização obrigatória

Migrar para o resolvedor:

- ações comuns;
- condições;
- trigger e options do Select2;
- ready de abas;
- success e protocol;
- download por clique;
- safe final confirmation;
- diagnóstico de frames;
- qualquer `Page.Locator` direto que represente elemento de negócio.

`BusySelectors` pode permanecer como configuração técnica bruta inicialmente.

### Critério de conclusão

Todos os comportamentos antigos podem ser recriados e executados no schema novo em modo estrito.

## 14. Fase 5 — Alternativas manuais

### Objetivo

Tentar candidatos ordenadamente sem heurística.

### Algoritmo

```text
Para cada candidato:
    construir a receita
    localizar frames e scope
    sondar quantidade e estado
    se for válido, devolver
    caso contrário, registrar o motivo
Se nenhum funcionar:
    retornar falha de resolução
```

### Motivos de falha

- frame ausente;
- scope ausente;
- target ausente;
- ambiguidade para `single`;
- coleção vazia quando era necessário encontrar itens;
- texto dinâmico vazio;
- estado incompatível;
- timeout;
- seletor inválido.

### Orçamento de tempo

Não aplicar o timeout inteiro para cada candidato. Deve existir:

- orçamento total da ação;
- sondagem curta por candidato;
- uso do tempo restante pelo candidato válido;
- registro da duração de cada tentativa.

### Opcionalidade

Uma ação opcional só pode desistir depois de esgotar os candidatos permitidos.

### Critério de conclusão

Se o principal falha e um alternativo funciona, a ação e a execução continuam normalmente.

## 15. Fase 6 — Captura de fingerprints

### Objetivo

Criar evidência estrutural para permitir recuperação futura.

### Quando capturar

Quando um candidato conhecido encontra corretamente um elemento singular:

- extrair fingerprint;
- guardar como observação provisória;
- confirmar apenas depois de `Succeeded`.

Também poderá existir captura durante a edição visual.

### Dados possíveis

- tag;
- ID;
- `name`;
- `type`;
- role;
- accessible name;
- `aria-*` relevante;
- `data-testid` e equivalentes;
- classes estáveis;
- texto limitado;
- ancestral estável;
- caminho relativo;
- irmãos próximos;
- posição aproximada;
- assinatura de página;
- frame e scope.

### Segurança

Não persistir automaticamente:

- valor de input;
- senha;
- token;
- cookie;
- HTML integral;
- texto longo;
- cabeçalho de autenticação;
- dado dinâmico sensível usado em `hasTextSource`.

### Critério de conclusão

Depois de execuções bem-sucedidas, os elementos singulares utilizados possuem fingerprints sanitizados.

## 16. Fase 7 — Heurística determinística

### Fonte obrigatória da adaptação

Antes de escrever o motor .NET, fixar o Scrapling no commit de referência e mapear estes comportamentos:

- `Selector.relocate(...)`: percorre os elementos do DOM, calcula a similaridade, agrupa empates, seleciona o maior score e só retorna resultado quando ele alcança `percentage`;
- `Selector.__calculate_similarity_score(...)`: calcula uma média dos fatores disponíveis;
- `Selector.__calculate_dict_diff(...)`: compara chaves e valores de atributos;
- `_StorageTools.element_to_dict(...)`: gera a representação persistível do elemento;
- `css(..., adaptive, auto_save, percentage)` e `xpath(...)`: primeiro tentam o seletor exato e somente depois recorrem à relocalização;
- armazenamento e recuperação por `identifier`, equivalente conceitual ao `locatorId` do RpaBlockly.

O algoritmo do Scrapling considerado nessa referência pontua:

- igualdade da tag;
- similaridade do texto;
- similaridade do conjunto de atributos;
- similaridade separada de `class`, `id`, `href` e `src`, quando existirem no original;
- similaridade do caminho estrutural;
- tag, atributos e texto do pai;
- sequência de irmãos.

O Scrapling usa `difflib.SequenceMatcher` nos campos textuais e retorna uma porcentagem média. O port .NET deverá implementar uma versão compatível e coberta por vetores de teste; não substituir silenciosamente por distância de Levenshtein, Jaro-Winkler ou outra função com distribuição de scores diferente.

### Harness Python de referência

Adicionar uma ferramenta apenas para desenvolvimento/testes, por exemplo:

```text
tools/scrapling-reference/
├── pyproject.toml
├── uv.lock ou requirements.lock
├── evaluate_fixture.py
├── generate_golden_files.py
└── README.md
```

Ela deverá:

1. instalar/pinar `scrapling==0.4.14` ou o commit exato;
2. receber fingerprint e HTML de fixture sanitizados;
3. executar a relocalização real do Scrapling;
4. emitir JSON com candidatos, scores, empates e vencedor;
5. gerar golden files versionados para os testes .NET;
6. nunca receber HTML, cookies ou credenciais de produção.

Os testes comuns do `dotnet test` devem usar golden files e não depender de Python. Uma suíte diferencial separada, executável no CI quando Python estiver disponível, deve regenerar os resultados e detectar mudanças inesperadas.

### Port nativo para o RpaBlockly

Criar no projeto de localização adaptativa componentes equivalentes, sem reproduzir a API Python:

```text
IElementFingerprintFactory
ISimilarityMetric
ScraplingCompatibleSequenceMatcher
ScraplingBaselineScorer
RpaSafetyAdjustedScorer
IAdaptiveCandidateCollector
AdaptiveLocatorEngine
AdaptiveResolutionPolicy
```

Separar o score-base compatível com Scrapling dos ajustes específicos do RpaBlockly. Isso permite provar o que veio da biblioteca e o que foi acrescentado pelo produto.

O port deverá converter o DOM vivo do Playwright para uma representação mínima e sanitizada. Não será necessário serializar a página inteira nem reconstruir o DOM com um parser Python. A coleta deverá ocorrer dentro do frame e scope já resolvidos, em uma única avaliação JavaScript limitada quando possível, retornando apenas os campos necessários para pontuar.

### Divergências intencionais e obrigatórias

O RpaBlockly **não copiará cegamente** todas as decisões do Scrapling:

- o Scrapling pode retornar todos os elementos empatados no maior score; uma ação singular do RpaBlockly deverá rejeitar empate não resolvido;
- além do threshold mínimo, o RpaBlockly exigirá `runnerUpGap` configurável;
- o vencedor deverá passar por cardinalidade, visibilidade, habilitação, tipo de ação, frame e scope;
- o candidato de maior score não será aceito se estiver abaixo do threshold;
- o fingerprint atualizado e a promoção ficarão provisórios até o sucesso da execução inteira;
- seletores alternativos explícitos serão tentados antes da heurística;
- coleções não serão tratadas como se fossem um único elemento;
- limites de nós, tempo, texto e atributos evitarão varreduras excessivas;
- tags e atributos sensíveis serão descartados antes da persistência e dos logs.

Cada divergência deverá aparecer no ADR “Scrapling adaptation”, nos nomes dos testes e na documentação do operador.

### Pipeline

1. determinar página e frame;
2. aplicar scope, quando disponível;
3. coletar candidatos dentro de limites;
4. eliminar incompatíveis;
5. calcular score;
6. ordenar;
7. verificar threshold;
8. verificar distância para o segundo colocado;
9. validar cardinalidade e estado;
10. aceitar ou falhar.

### Componentes do score

- tag;
- ID;
- atributo estável;
- role;
- accessible name;
- classes estáveis;
- texto;
- ancestral;
- posição relativa;
- irmãos;
- penalização de atributos voláteis;
- penalização de elemento oculto, desabilitado ou incompatível.

### Regra de aceitação

```text
score >= threshold
e
scorePrimeiro - scoreSegundo >= runnerUpGap
```

O maior score abaixo do limite não será aceito.

### Materialização do aprendizado

Tentar gerar, em ordem:

1. test ID;
2. ID estável;
3. role e nome acessível;
4. atributo estável;
5. combinação curta de atributos;
6. scope estável e target;
7. candidato do tipo `fingerprint`.

### Coleções

A heurística singular não deve fingir que recupera coleções. `readElements`, `typeAcrossInputs` e options de Select2 precisam de uma fase própria baseada em:

- fingerprint do container;
- padrão dos filhos;
- cardinalidade mínima;
- amostras;
- validação da coleção.

Até essa extensão existir, coleções continuam funcionando com candidatos exatos e alternativos.

### Estados negativos

`hidden` e `detached` não devem aprender automaticamente. A ausência pode significar tanto sucesso quanto seletor quebrado.

### Critério de conclusão

O motor recupera mudanças controladas, recusa páginas ambíguas ou com baixa confiança e possui evidência automatizada de que o score-base foi adaptado corretamente do Scrapling.

## 17. Fase 8 — Aprendizado provisório por execução

### Estrutura

Adicionar ao `RpaContext`:

```text
LocatorLearningSession
├── provisionalOverrides
├── pendingFingerprintUpdates
├── pendingPromotions
└── resolutionHistory
```

### Comportamento

Quando a execução E1 encontra H1:

- H1 vira a primeira tentativa apenas de E1;
- uma ação posterior de E1 que use o mesmo locator tenta H1 primeiro;
- outras execuções em andamento não são alteradas;
- nenhuma escrita permanente ocorre nesse momento.

Se H1 falhar mais adiante:

- E1 continua pelos candidatos restantes;
- pode descobrir H2;
- a mutação provisória é atualizada;
- o histórico registra a instabilidade.

### Descarte

Descartar em:

- falha;
- cancelamento;
- timeout global;
- `SafeValidation`;
- encerramento inesperado.

### Critério de conclusão

Uma mesma execução reutiliza sua própria recuperação sem interferir nas demais.

## 18. Fase 9 — Promoção confirmada

### Regra move-to-front

Ordem inicial:

```text
[A original, B manual, C manual]
```

Se B recuperar e a execução terminar como `Succeeded`:

```text
[B manual, C manual, A original]
```

Se H for descoberto:

```text
[H heurístico, B manual, C manual, A original]
```

### Regras

- vencedor vai para o início;
- principal que falhou vai para o fim;
- intermediários preservam ordem relativa;
- origem e papel original não mudam;
- não duplicar candidato;
- identificar por ID, nunca por índice;
- confirmar somente em `Succeeded`.

### Critério de conclusão

Depois da publicação, execuções novas podem usar o vencedor primeiro; execuções já iniciadas continuam independentes com seus snapshots anteriores.

## 19. Fase 10 — Registry de snapshots no worker

### Objetivo

Evitar leitura de pacote durante ações e permitir atualização por revisão.

### Estrutura

```text
RpaPackageRuntimeRegistry
  RpaCode → RpaPackageRuntimeState
```

Cada estado contém:

- snapshot atual para novas execuções;
- revisão/hash;
- origem;
- lock apenas para troca/publicação do documento;
- nenhuma sincronização das etapas de execução.

### Início do item

```text
ClaimNextAsync devolve item
        ↓
worker consulta a revisão indicada para o pacote
        ↓
registry reutiliza ou carrega snapshot
        ↓
ProcessAsync recebe uma cópia imutável
```

Depois desse ponto, a execução não consulta o registry novamente para mudar sua definição.

### SQL sem TTL

Quando a origem for SQL, o claim poderá retornar também `PackageRevision`. Como a consulta de claim já existe:

- revisão igual à carregada: reutilizar snapshot;
- revisão diferente: carregar o JSON uma vez;
- nenhuma consulta por ação;
- nenhum TTL para adivinhar validade.

### Arquivo

Opções:

- carregar no startup;
- verificar hash/manifest antes de cada execução;
- recarregar quando a revisão do arquivo mudar.

### Critério de conclusão

Cada execução recebe exatamente uma revisão estável e não precisa acompanhar alterações externas enquanto está rodando.

## 20. Fase 11 — Concorrência independente

### Objetivo

Permitir N execuções simultâneas do mesmo RPA sem barreira ou espera.

### Não implementar

- `SequentialPerRpa`;
- `MaxParallelismPerRpa = 1` obrigatório;
- semaphore por `RpaCode` envolvendo a execução;
- lane exclusiva por RPA;
- sincronização de ações;
- espera para que todas estejam na mesma etapa;
- cancelamento de execução antiga porque surgiu uma revisão nova.

### Comportamento correto

```text
E1 inicia com revisão 10 e aprende H1 provisoriamente
E2 inicia com revisão 10 e continua com sua própria resolução
E2 termina primeiro e publica H2 como revisão 11
E3 inicia e recebe revisão 11
E1 termina depois e tenta publicar H1 sobre a revisão atual
```

E1 não deve sobrescrever cegamente a revisão 11. Ela envia uma mutação semântica:

```text
PromoteCandidate(locatorId, candidateId, failedPrimaryId, baseRevision, executionId)
```

O store recarrega/rebaseia a mutação quando necessário.

### Conflito entre aprendizados

Se E1 e E2 aprenderem o mesmo candidato:

- unir metadados;
- não duplicar;
- manter o candidato no início;
- registrar ambas as evidências.

Se aprenderem candidatos diferentes para o mesmo locator:

- manter ambos;
- a última promoção confirmada e efetivamente gravada torna-se o principal;
- o candidato anterior permanece como fallback;
- registrar a alternância para detectar flapping.

Se houver edição manual concorrente:

- alteração do desenvolvedor vence;
- aprendizado conflitante não reorganiza aquele locator automaticamente;
- registrar evento para revisão.

### Continuidade dos demais itens

- recuperação por fallback ou heurística não lança falha;
- uma execução que falhe de forma irrecuperável não interrompe as demais;
- `ProcessSafelyAsync` continua isolando falhas por WorkItem;
- o paralelismo global continua controlado por `MaxParallelism`.

### Critério de conclusão

Testes demonstram execuções do mesmo RPA em etapas e durações diferentes, sem bloqueio mútuo e sem corrupção do catálogo.

## 21. Fase 12 — Write-back configurável

### Modos

#### `Disabled`

- sem promoção persistente;
- comportamento estrito ou fallback somente conforme a política.

#### `Memory`

- alterações confirmadas atualizam somente o registry do processo;
- outras execuções já iniciadas não mudam;
- execuções futuras naquele processo podem usar a atualização;
- reinício remove o estado.

#### `Source`

- origem arquivo: regrava `locators.production.json`;
- origem SQL: atualiza o JSON do pacote;
- origem inline: inválido porque a origem é somente leitura.

#### `Overlay`

- pacote base permanece intacto;
- aprendizado é persistido em outro provider;
- base e overlay são mesclados ao carregar;
- combinação precisa ser explícita.

### Configuração

```json
{
  "Package": {
    "Provider": "File",
    "Reference": "rpas/portal-cliente",
    "RefreshPolicy": "RevisionAware"
  },
  "Learning": {
    "WriteBack": "Source"
  }
}
```

### Critério de conclusão

O mesmo executor funciona com qualquer provider sem saber se o documento veio de arquivo ou SQL.

## 22. Fase 13 — Provider SQL

### Tabelas

```text
RpaPackage
- RpaCode
- CurrentRevision
- FlowJson
- LocatorsJson
- PolicyJson
- ContentHash
- RowVersion
- UpdatedAtUtc
- UpdatedBy
- ChangeOrigin

RpaPackageHistory
- RpaCode
- Revision
- FlowJson
- LocatorsJson
- PolicyJson
- ContentHash
- CreatedAtUtc
- ChangeOrigin
- ExecutionId
```

Opcional:

```text
LocatorLearningEvent
- ExecutionId
- RpaCode
- BaseRevision
- LocatorId
- CandidateId
- MutationJson
- Status
- CreatedAtUtc
- AppliedAtUtc
```

Essa tabela é histórico/outbox, não a fonte da ordem efetiva.

### Compare-and-swap

```sql
UPDATE RpaPackage
SET
    LocatorsJson = @locators,
    CurrentRevision = CurrentRevision + 1
WHERE
    RpaCode = @rpaCode
    AND CurrentRevision = @expectedRevision;
```

Se não atualizar:

1. carregar revisão atual;
2. verificar mudança manual;
3. reaplicar a mutação por IDs;
4. validar;
5. tentar novamente uma vez;
6. gerar warning se não for possível.

### Critério de conclusão

Execuções concorrentes não perdem candidatos nem sobrescrevem silenciosamente mudanças manuais.

## 23. Fase 14 — Editor Blockly

### Divisão do JavaScript

O `app.js` atual deve ser dividido:

```text
wwwroot/js/
├── api.js
├── blocks.js
├── flow-mapper.js
├── locator-field.js
├── locator-catalog.js
├── locator-drawer.js
├── workspace.js
└── app.js
```

### Campo customizado

Criar `FieldLocatorReference`.

Valor interno:

```text
login.submit
```

Exibição:

```text
🎯 Botão Entrar
ATUAL · HEURÍSTICO · [data-testid='login-submit'] · +3
```

### Popover

Mouse, teclado ou toque mostra:

```text
Botão Entrar
ID: login.submit

1. [data-testid='login-submit']
   ATUAL · APRENDIDO POR HEURÍSTICA

2. button[name='entrar']
   ALTERNATIVA DO DESENVOLVEDOR

3. #login > button.primary
   ORIGINAL DO DESENVOLVEDOR
```

### Drawer

- nome e ID;
- ordem atual;
- origem;
- receita integral;
- frames e scope;
- filtros;
- fingerprints;
- histórico;
- copiar;
- adicionar;
- editar;
- reordenar;
- restaurar ordem original;
- remover aprendido;
- tornar principal.

### Catálogo

- pesquisa;
- origem;
- locators sem uso;
- compartilhados;
- recuperados recentemente;
- baixa confiança;
- falhas recentes.

### Papéis distintos

Blocos complexos mantêm referências separadas:

```text
Select2
├── select nativo
├── controle visível
└── opções exibidas
```

### API

```text
GET  /api/package
GET  /api/flow
PUT  /api/flow
GET  /api/locators
GET  /api/locators/{id}
PUT  /api/locators/{id}
POST /api/locators/{id}/make-primary
POST /api/locators/{id}/restore-developer-order
DELETE /api/locators/{id}/candidates/{candidateId}
GET  /api/locators/{id}/history
GET  /api/policy
PUT  /api/policy
```

### Critério de conclusão

O round-trip não perde nenhuma referência, papel, receita, tag ou candidato.

## 24. Fase 15 — Diagnósticos e artefatos

### Eventos

```text
locatorResolutionStarted
locatorCandidateFailed
locatorCandidateSucceeded
locatorRecoveredByFallback
locatorHeuristicStarted
locatorHeuristicRejected
locatorRecoveredByHeuristic
locatorProvisionalPromotion
locatorPromotionCommitted
locatorPromotionRebased
locatorPromotionDiscarded
locatorLearningPersistenceFailed
```

### Dados

- execution ID;
- package revision;
- locator ID e papel;
- action ID;
- candidato e origem;
- posição;
- seletor sanitizado;
- quantidade;
- motivo;
- duração;
- score e runner-up gap;
- URL e frame;
- ordem antes/depois.

### Falha

Capturar:

- screenshot;
- HTML sanitizado;
- URL;
- relatório JSON de tentativas;
- fingerprint utilizado;
- candidatos heurísticos;
- revisão e hash.

O `RpaRunner` atual captura screenshot, mas o worker só materializa artefatos depois de sucesso. Isso precisa ser corrigido.

### HTML

- tamanho limitado;
- compressão opcional;
- valores de password removidos;
- tokens sanitizados;
- retenção configurável.

### Critério de conclusão

Uma falha pode ser investigada sem depender de reprodução imediata.

## 25. Fase 16 — Retry e efeitos colaterais

O `FailAsync` atual considera apenas `AttemptCount < MaxAttempts`. Deve também considerar `FlowExecutionFailure.Retryable` e efeitos irreversíveis.

### Regras

- configuração inválida: sem retry;
- browser temporariamente indisponível: retry;
- locator esgotado: conforme política;
- depois de ação irreversível: sem retry automático;
- falha de write-back: execução continua `Succeeded`;
- falha do observer ou notificação: não interrompe negócio.

### Critério de conclusão

Nenhuma execução de negócio é repetida apenas porque a persistência do aprendizado ou uma notificação falhou.

## 26. Fase 17 — Notificações

```csharp
public interface IRpaNotificationSink
{
    Task NotifyAsync(
        RpaNotification notification,
        CancellationToken cancellationToken);
}
```

Notificar opcionalmente:

- falha da execução;
- recuperação por heurística;
- promoção;
- conflito;
- falha de persistência;
- confiança próxima do limite;
- disponibilidade de artefatos.

Inicialmente: log estruturado e outbox. Depois: webhook, e-mail, Teams ou Slack.

Notificação nunca bloqueia a execução.

## 27. Fase 18 — Segurança e limites

### Pacotes externos

Se outro serviço ou uma LLM produzir o JSON, o executor só enxerga um pacote. Validar:

- tamanho;
- quantidade de ações;
- profundidade;
- tipos permitidos;
- URLs e métodos HTTP;
- timeouts;
- limites de heurística;
- quantidade de candidatos;
- profundidade de frames;
- caminhos de arquivo;
- referências a segredos.

### Heurística

Limitar:

- nós examinados;
- duração;
- tamanho de texto;
- atributos;
- resultados;
- tentativas.

A política global do host é o teto de segurança. Um pacote pode ser mais restritivo, mas não pode liberar algo proibido pelo worker.

## 28. Fase 19 — Testes automatizados

### Contratos

- JSON válido/inválido;
- propriedade desconhecida;
- locator inexistente;
- ID duplicado;
- receita incompleta;
- múltiplos originais;
- literal e source simultâneos;
- provider/write-back incompatíveis.

### Equivalência

Executar locator antigo e receita nova na mesma fixture e comparar identidade/quantidade para:

- CSS;
- XPath;
- raw Playwright;
- scope;
- filtros;
- fontes dinâmicas;
- frames;
- `single`;
- `first`;
- `many`;
- condições;
- trigger/options;
- ready;
- success/protocol.

### Fallback

- principal funciona;
- segundo funciona;
- terceiro funciona;
- ambíguo é rejeitado;
- opcional esgota alternativas antes de desistir;
- orçamento total não multiplica pelo número de candidatos.

### Heurística

- golden files gerados pelo Scrapling `v0.4.14`/commit fixado;
- paridade do `SequenceMatcher` para strings, listas e sequências vazias;
- paridade do score-base para tag, texto, atributos, caminho, pai e irmãos;
- empate no maior score retornado pelo Scrapling, mas rejeitado pelo modo singular seguro do RpaBlockly;
- teste diferencial opcional executando o harness Python e o scorer .NET na mesma fixture;
- divergências intencionais cobertas e documentadas;
- ID alterado;
- classe alterada;
- elemento movido;
- texto alterado;
- candidatos parecidos;
- score baixo;
- gap insuficiente;
- ausência de fingerprint;
- sanitização.

### Aprendizado local à execução

- E1 descobre H1;
- E1 usa H1 novamente;
- E2 em andamento não recebe H1;
- E1 falha depois e descarta H1;
- `Validated` descarta;
- `Succeeded` confirma.

### Concorrência independente

1. E1 e E2 começam com a mesma revisão.
2. Elas avançam em velocidades diferentes.
3. E2 publica primeiro.
4. E1 continua sem trocar seu snapshot.
5. E3 começa depois e recebe a revisão nova.
6. E1 publica uma mutação concorrente por compare-and-swap.
7. Nenhuma execução espera outra.

Também testar:

- mesmo candidato aprendido por duas execuções;
- candidatos diferentes para o mesmo locator;
- edição manual concorrente;
- conflito de arquivo;
- conflito SQL;
- múltiplos workers;
- detecção de flapping.

### Editor

- flow → Blockly → flow;
- catálogo → UI → catálogo;
- todos os papéis;
- IDs não exibidos sozinhos;
- teclado e toque;
- Unicode;
- XPath com aspas;
- strings não alteradas.

### Artefatos

- screenshot em falha;
- HTML em falha;
- sanitização;
- persistência;
- falha auxiliar não oculta erro original.

## 29. Conversor offline

Criar:

```text
tools/RpaFlow.Migrator
```

Esse projeto pode conter DTOs do schema antigo, mas não será referenciado pelo worker novo.

### Entrada e saída

```text
schema 1
   ↓
flow schema 2
locators schema 1
relatório de migração
```

### IDs iniciais

```text
{actionId}.target
{actionId}.trigger
{actionId}.options
{actionId}.ready
{actionId}.success
{actionId}.protocol
{actionId}.condition
```

Não deduplicar automaticamente seletores iguais. Dois usos com a mesma string podem representar elementos semanticamente diferentes.

### Relatório

- ações convertidas;
- locators criados;
- usos `first`;
- coleções;
- seletores especiais;
- duplicidades potenciais;
- itens que exigem revisão humana;
- caminho de origem de cada locator.

### Critério de conclusão

Todo campo de localização do schema anterior possui conversão mecânica para uma receita nova, mesmo que a consolidação semântica exija trabalho humano posterior.

## 30. Ordem recomendada dos pull requests

1. ADRs, glossário, matriz de expressividade e ADR da adaptação do Scrapling.
2. Contratos e schema novos.
3. Validador de pacote.
4. `RpaFlow.Packages` e provider de arquivos.
5. Resolvedor estrito.
6. Migração de todos os handlers para o resolvedor.
7. Testes de equivalência.
8. Alternativas manuais.
9. Eventos estruturados.
10. Fingerprints e harness Python do Scrapling com golden files.
11. Score-base compatível, ajustes de segurança e heurística singular.
12. Aprendizado provisório independente por execução.
13. Promoção e write-back em arquivo.
14. Registry de snapshots por revisão.
15. Concorrência otimista sem serialização das execuções.
16. Provider SQL e merge de conflitos.
17. API do editor.
18. Campo Blockly e catálogo visual.
19. Artefatos de falha.
20. Retry e notificações.
21. Conversor offline.
22. Templates, exemplo, manual e remoção definitiva do schema 1.

Cada PR deve deixar a solução compilando e os testes acumulados passando.

## 31. Arquivos atuais mais afetados

### Contratos

- `src/RpaFlow.Contracts/Flow/FlowDefinition.cs`
- `src/RpaFlow.Contracts/Flow/FlowDefinitionValidator.cs`
- `src/RpaFlow.Contracts/Flow/FlowJsonSerializer.cs`
- `src/RpaFlow.Contracts/Flow/JsonFlowLoader.cs`
- novos arquivos de catálogo, política e pacote.

### Playwright

- `src/RpaFlow.Playwright/Flow/FlowLocatorFactory.cs`
- `src/RpaFlow.Playwright/Flow/FlowActionExecutionScope.cs`
- `src/RpaFlow.Playwright/Flow/FlowConditionEvaluator.cs`
- handlers de formulário, navegação, dados, download e confirmação;
- `src/RpaFlow.Playwright/Core/RpaContext.cs`
- `src/RpaFlow.Playwright/Core/RpaRunner.cs`
- `src/RpaFlow.Playwright/Core/ExecutionArtifacts.cs`.

### Worker

- `src/Rpa.Worker/Configuration/RpaWorkerOptions.cs`
- `src/Rpa.Worker/Configuration/RpaWorkerOptionsValidator.cs`
- `src/Rpa.Worker/Execution/RpaBackgroundService.cs`
- `src/Rpa.Worker/Execution/WorkItemProcessor.cs`
- `src/Rpa.Worker/Data/SqlWorkItemRepository.cs`
- `src/Rpa.Worker/Domain/WorkerModels.cs`
- `src/Rpa.Worker/Program.cs`
- `src/Rpa.Worker/appsettings.example.json`.

### Editor

- `src/RpaFlow.Editor/Program.cs`
- `src/RpaFlow.Editor/Services/ProjectFileService.cs`
- `src/RpaFlow.Editor/Configuration/EditorProfile.cs`
- `src/RpaFlow.Editor/Configuration/EditorPaths.cs`
- `src/RpaFlow.Editor/wwwroot/app.js`
- `src/RpaFlow.Editor/wwwroot/index.html`
- `src/RpaFlow.Editor/wwwroot/styles.css`.

### Banco, templates e testes

- `database/sqlserver/001_create_worker_schema.sql` ou novas migrations incrementais;
- `tools/scrapling-reference` para o harness Python exclusivamente de desenvolvimento/teste;
- `THIRD-PARTY-NOTICES.md` e ADR com a atribuição, commit e divergências do Scrapling;
- `templates/rpa-web`;
- `examples/RpaExemplo`;
- todos os projetos em `tests`;
- `docs`;
- `AGENTS.md`;
- `README.md`.

## 32. Itens fora do escopo

- adaptação dos três RPAs reais;
- integração direta com LLM;
- prompts, modelos e tokens;
- sincronização de etapas entre execuções;
- garantia de ordem entre WorkItems concorrentes;
- atualização oportunista de .NET ou Playwright;
- refatorações sem relação com localização, diagnóstico ou armazenamento do pacote.

Os RPAs antigos podem continuar temporariamente no binário atual enquanto a versão nova é construída. O cutover de cada um ocorrerá somente quando sua reescrita posterior estiver validada.

## 33. Definição final de pronto

A implementação estará pronta quando:

1. o runtime aceitar somente o schema novo;
2. todo mecanismo de localização antigo tiver equivalente comprovado;
3. nenhum handler ler seletor bruto diretamente da ação;
4. todo elemento de negócio passar pelo `LocatorResolver`;
5. o resolvedor não fizer I/O durante ações;
6. alternativas forem tentadas antes da heurística;
7. heurística respeitar threshold e runner-up gap;
8. cada execução mantiver seu próprio snapshot;
9. execuções simultâneas do mesmo RPA não esperarem umas pelas outras;
10. aprendizado provisório afetar apenas a execução que o descobriu;
11. promoção ocorrer somente depois de `Succeeded`;
12. execuções novas poderem receber a revisão promovida;
13. execuções antigas continuarem com sua revisão original;
14. mutações concorrentes serem rebaseadas por IDs e compare-and-swap;
15. edição manual vencer conflitos com aprendizado;
16. ordem poder ser persistida em arquivo ou SQL;
17. nenhuma consulta de locator ocorrer por ação;
18. não existir dependência de TTL;
19. falha de write-back não repetir negócio;
20. screenshot, HTML e tentativas serem persistidos em falha;
21. Blockly apresentar nome e seletor de forma amigável;
22. o conversor offline representar todos os campos antigos;
23. testes provarem concorrência independente, tempos diferentes e snapshots imutáveis;
24. o score-base .NET possuir golden files e testes diferenciais contra o Scrapling fixado;
25. a solução documentar claramente o código adaptado, a licença BSD 3-Clause e todas as divergências intencionais;
26. o worker de produção executar a heurística sem depender de Python.

O princípio central é: **cada execução é isolada e autônoma; o compartilhamento ocorre apenas pela publicação versionada de conhecimento confirmado para execuções futuras**.
