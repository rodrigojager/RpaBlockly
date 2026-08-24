# Plano mestre de implementação — RpaBlockly V2 e Recorder para Chrome

## 1. Propósito e ordem obrigatória

Este documento transforma os dois planos de origem em uma sequência única, executável e verificável:

- [Plano de seletores resilientes](plano-implementacao-rpablockly-resiliencia-seletores.md);
- [Plano do RpaBlockly V2 Recorder](PLANO_IMPLEMENTACAO_RPABLOCKLY_V2_RECORDER.md).

A ordem de entrega é obrigatória:

```text
RpaBlockly atual (schema 1)
        ↓
Contratos, pacote, runtime, worker e editor V2
        ↓
Migrador offline V1 → V2 e migração dos exemplos
        ↓
Release candidate funcional da V2
        ↓
Extensão Recorder baseada somente nos contratos V2 estáveis
        ↓
Importação, revisão e execução ponta a ponta
```

Nenhum código do Recorder deve ser iniciado antes da aprovação do gate `V2-G13`. Essa regra evita criar uma extensão dependente de contratos provisórios ou reproduzir no TypeScript o modelo antigo de seletores embutidos nas ações.

## 2. Estado de partida confirmado

Baseline auditada em 17 de agosto de 2026:

- diretório exclusivo de trabalho: a raiz deste repositório;
- branch de trabalho: `feature/rpablockly-v2`;
- commit de partida: `03b74fe2197a`;
- runtime: .NET 9;
- solução: `RpaBlockly.slnx`;
- contrato operacional atual: `flow.production.json` com `schemaVersion: 1`;
- catálogo atual: 32 tipos de ação no runtime e 35 blocos distintos no editor;
- editor: aplicação local com `app.js` monolítico de aproximadamente 118 KB;
- persistência do editor: apenas configuração e fluxo, com UTF-8 estrito, backup e troca atômica de arquivo;
- worker: já reserva itens individualmente e executa até `MaxParallelism` casos independentes;
- resolução atual: `FlowLocatorFactory` cobre boa parte dos alvos, mas alguns seletores auxiliares ainda chegam diretamente a `Page.Locator`;
- armazenamento atual: caminho de fluxo e configuração definido por RPA, sem pacote, revisão ou compare-and-swap;
- ausentes: schemas V2, catálogo separado de localizadores, política separada, package stores, provider SQL de pacotes, migrador V1 → V2, extensão e importador Recorder.

Nenhuma fase deste plano autoriza editar arquivos fora da raiz deste repositório.

### 2.1 Consequências desta auditoria

1. Não recriar o paralelismo do worker. O trabalho V2 é garantir que cada caso obtenha um snapshot imutável de uma revisão, sem barreiras entre instâncias.
2. Não usar contagens antigas de 23 tipos ou 26 blocos como baseline. A migração deve cobrir os 32 tipos e preservar os 35 blocos atuais, salvo decisão explícita em ADR.
3. Não substituir tudo de uma vez. Os componentes V2 serão construídos lado a lado com os tipos V1 até o migrador e os testes de equivalência estarem prontos.
4. O runtime V2 não desserializará schema 1. Durante o desenvolvimento, o código V1 permanece apenas para manter a branch verificável; depois do cutover, o schema antigo fica isolado no migrador e nas fixtures históricas.
5. A extensão não criará um formato intermediário. Ela produzirá diretamente os três documentos operacionais V2 dentro de um bundle versionado.

## 3. Limites de escopo

### 3.1 Incluído

- pacote V2 com fluxo, localizadores e política separados;
- resolução centralizada e determinística de localizadores;
- fallback manual, heurística segura e aprendizado confirmado;
- snapshots e revisões para arquivo, memória, inline e SQL Server;
- editor visual V2;
- migrador offline do schema 1 para a V2;
- migração dos exemplos e templates deste repositório;
- extensão Chrome Manifest V3;
- bundle `.rpablockly.zip`;
- importação segura, preview e aplicação atômica pelo editor;
- testes de contrato, segurança, concorrência, determinismo e ponta a ponta.

### 3.2 Excluído

- contrato específico de cliente externo, seus IDs, seletores, mensagens, ordem de envios ou regras de conclusão;
- generalização adicional dos mecanismos abstratos discutidos no item 6 anterior;
- regras, fluxos, credenciais, dados ou requisitos provenientes de outros repositórios;
- execução ou replay do RPA dentro da extensão;
- captura de cookies, storage do site, headers ou tráfego de rede;
- companion app, native messaging, `chrome.debugger`, LLM ou geração probabilística;
- conversão automática de casos não suportados em ações supostamente equivalentes.

As proteções genéricas já existentes, como o limite de validação segura e a classificação de ações irreversíveis, devem ser preservadas. Esta iniciativa não deve ampliá-las para reproduzir o contrato concreto excluído.

## 4. Arquitetura final

```text
Extensão Chrome MV3
  captura determinística + evidências + criptografia
                    │
                    ▼
          bundle .rpablockly.zip
                    │
                    ▼
Editor V2 ── inspeção segura ── preview ── aplicação atômica
                    │
                    ▼
RpaPackageSnapshot
  ├── flow.production.json       schema 2
  ├── locators.production.json   schema 1
  └── rpa.policy.json            schema 1
                    │
                    ▼
IRpaPackageStore / RpaPackageRuntimeRegistry
  ├── File
  ├── SQL Server
  ├── Memory
  └── Inline somente leitura
                    │
                    ▼
PlaywrightFlowExecutor → LocatorResolver → Playwright
```

### 4.1 Organização alvo do repositório

```text
schemas/
  flow-v2.schema.json
  locators-v1.schema.json
  rpa-policy-v1.schema.json
  recorder-bundle-v1.schema.json
  recorder-session-v1.schema.json
  recorder-evidence-v1.schema.json
  recorder-issues-v1.schema.json

src/
  RpaFlow.Contracts/
  RpaFlow.Runtime/
  RpaFlow.Packages/
  RpaFlow.Packages.SqlServer/
  RpaFlow.Playwright/
  RpaFlow.Editor/
  Rpa.Worker/
  RpaFlow.Recorder.Extension/

tests/
  RpaFlow.ContractsChecks/
  RpaFlow.PackagesChecks/
  RpaFlow.PlaywrightChecks/
  RpaFlow.EditorRoundTrip/
  RpaFlow.EditorRecorderImportChecks/
  RpaFlow.RecorderContractChecks/
  Rpa.WorkerChecks/
  recorder-extension-e2e/

tools/
  RpaFlow.Migrator/
  scrapling-reference/
```

## 5. Decisões arquiteturais a formalizar antes do código

Criar os ADRs abaixo em `docs/adr/`. Cada ADR deve registrar contexto, decisão, alternativas recusadas, consequência, estratégia de rollback e testes que comprovam a decisão.

| ID | Decisão | Padrão proposto |
|---|---|---|
| ADR-001 | Fronteira V1/V2 | V2 não desserializa V1; conversão somente offline |
| ADR-002 | Documentos do pacote | fluxo, localizadores e política separados |
| ADR-003 | Fonte canônica | JSON Schemas versionados + fixtures compartilhadas |
| ADR-004 | JSON determinístico | UTF-8 sem BOM, LF, chaves ordenadas e arrays preservados |
| ADR-005 | Revisão e concorrência | revisão opaca + hash + compare-and-swap |
| ADR-006 | Snapshot de execução | imutável, carregado antes da primeira ação |
| ADR-007 | Resolução | toda localização de negócio passa por `LocatorResolver` |
| ADR-008 | Heurística | implementação C# nativa, Scrapling apenas como referência verificável |
| ADR-009 | Aprendizado | provisório por execução; commit somente em `Succeeded` |
| ADR-010 | Write-back | `Disabled`, `Memory`, `Source` e `Overlay` |
| ADR-011 | Migração | IDs determinísticos, sem deduplicação automática |
| ADR-012 | Bundle Recorder | pacote V2 nativo, sem schema intermediário e sem replay |
| ADR-013 | Chrome mínimo | Chrome 116 ou superior para o fluxo completo do side panel |
| ADR-014 | Permissões | mínimas e host permissions opcionais por gesto do usuário |
| ADR-015 | Segredos | captura desligada; AES-256-GCM + RSA-OAEP-SHA-256 |
| ADR-016 | Retenção | evidências e staging expiram por política explícita |
| ADR-017 | ZIP | limites, integridade e defesa contra Zip Slip/Zip Bomb |
| ADR-018 | Importação | inspect/preview não alteram estado; apply é atômico |

### Gate V2-G0 — arquitetura aprovada

- ADRs 001 a 011 aprovados;
- matriz de expressividade V1 → V2 revisada;
- catálogo real de 32 ações exportado por teste, sem contagem manual duplicada;
- fixtures V1 de referência livres de segredos;
- nenhum artefato específico de cliente incluído.

## 6. Trilha A — implementação completa da V2

### Fase V2-1 — baseline executável e proteção contra regressão

Dependência: `V2-G0`.

#### Tarefas

- `V2-001` — inventariar automaticamente tipos de ação, blocos, handlers, campos de localização e condições.
- `V2-002` — criar ao menos uma fixture V1 por família de ação e uma fixture agregada com os 32 tipos.
- `V2-003` — registrar os seletores auxiliares atuais: alvo, condição, trigger, options, ready, success, protocol e download.
- `V2-004` — medir e salvar o comportamento atual: validação, serialização, round-trip do editor e execução em páginas-fixture.
- `V2-005` — criar teste que falha se um tipo do catálogo não estiver coberto pela matriz de migração.
- `V2-006` — adicionar CI para build e executáveis de checks já existentes.

#### Entregas

- inventário versionado;
- golden files V1 sanitizados;
- relatório de cobertura do catálogo;
- baseline verde na CI.

#### Gate V2-G1

Todo recurso atual que possa perder expressividade possui fixture e expectativa observável antes da introdução dos tipos V2.

### Fase V2-2 — schemas e contratos V2

Dependência: `V2-G1`.

#### Tarefas

- `V2-010` — criar `flow-v2.schema.json` sem propriedades de seletor nas ações.
- `V2-011` — criar `locators-v1.schema.json` com candidatos ordenados, receita completa e fingerprints.
- `V2-012` — criar `rpa-policy-v1.schema.json` com modos strict, fallback e adaptive.
- `V2-013` — representar em `LocatorUseDefinition` o `locatorId` e a cardinalidade `single`, `first` ou `many`.
- `V2-014` — representar todos os papéis auxiliares: target, trigger, options, ready, success, protocol e condition.
- `V2-015` — implementar estratégias `css`, `xpath`, `role`, `label`, `placeholder`, `text`, `testId`, `rawPlaywright` e `fingerprint`.
- `V2-016` — manter `rawPlaywright` como rota de paridade para qualquer expressão aceita pelo modelo atual.
- `V2-017` — criar DTOs C# V2 lado a lado com os DTOs V1, sem misturar propriedades antigas.
- `V2-018` — configurar rejeição de propriedades desconhecidas e UTF-8 estrito.
- `V2-019` — implementar validações locais: IDs únicos sem diferença de caixa, candidato principal na primeira posição, receita com target, exclusão entre literal/source e ordem de frames.
- `V2-020` — criar serialização canônica e golden files válidos e inválidos.
- `V2-021` — gerar tipos TypeScript dos schemas, ainda sem criar a extensão.
- `V2-022` — criar teste de conformidade cruzada entre fixtures, C# e TypeScript.

#### Gate V2-G2 — contratos congelados para o runtime

- os 32 tipos atuais são representáveis;
- todos os campos de localização V1 têm destino mecânico documentado;
- fluxo V2 não contém seletor bruto embutido na ação;
- schemas, C# e TypeScript concordam sobre nomes, enums e obrigatoriedade;
- alterações posteriores incompatíveis exigem nova versão de schema.

### Fase V2-3 — modelo de pacote e validação cruzada

Dependência: `V2-G2`.

#### Tarefas

- `V2-030` — criar `RpaFlow.Packages`.
- `V2-031` — definir `RpaPackageSnapshot`, `PackageRevision`, `RpaPackageOrigin` e `ContentHash`.
- `V2-032` — implementar validação conjunta de fluxo, localizadores e política.
- `V2-033` — validar toda referência de locator, inclusive condições e subfluxos.
- `V2-034` — validar cardinalidade por ação: click não aceita `many`; coleções exigem `many`; usos opcionais mantêm a semântica da ação.
- `V2-035` — detectar ciclos de subfluxo e referências órfãs.
- `V2-036` — emitir warning, sem invalidar, para locator não utilizado.
- `V2-037` — validar combinações de origem, política, promoção e write-back.
- `V2-038` — calcular hash depois da validação e antes de publicar o snapshot.
- `V2-039` — garantir que o snapshot exponha somente coleções imutáveis ou cópias defensivas.

#### Gate V2-G3

Nenhum executor consegue ser criado a partir de três documentos inconsistentes, e nenhuma ação consegue alterar o snapshot recebido.

### Fase V2-4 — stores de arquivo, memória e inline

Dependência: `V2-G3`.

#### Tarefas

- `V2-040` — definir interfaces separadas de leitura, escrita, histórico e resolução da revisão atual.
- `V2-041` — implementar `FileRpaPackageStore` com layout documentado por RPA e ambiente.
- `V2-042` — escrever os três documentos em staging, validar novamente e publicar atomicamente.
- `V2-043` — manter backup e histórico mínimo por revisão.
- `V2-044` — implementar compare-and-swap para impedir sobrescrita silenciosa.
- `V2-045` — implementar `MemoryRpaPackageStore` para testes e modo de aprendizado em memória.
- `V2-046` — implementar `InlineRpaPackageSource` somente leitura.
- `V2-047` — testar falha entre cada etapa de gravação para provar que o pacote anterior continua íntegro.
- `V2-048` — testar processos concorrentes tentando atualizar a mesma revisão.

#### Gate V2-G4

Leitura sempre retorna um pacote completo de uma única revisão; falha ou concorrência nunca deixa mistura de documentos.

### Fase V2-5 — `LocatorResolver` estrito e migração dos handlers

Dependências: `V2-G3` e `V2-G4`.

#### Tarefas

- `V2-050` — definir a interface do resolver e os tipos de request, resultado, tentativa e falha.
- `V2-051` — compilar receita na ordem frames externos → internos, scope, filtro de scope, target e filtro de target.
- `V2-052` — mapear cada estratégia para a API apropriada do Playwright.
- `V2-053` — resolver `literal` e `source` somente pelo contexto de dados da execução.
- `V2-054` — aplicar cardinalidade e checagens de estado antes de devolver um alvo utilizável.
- `V2-055` — migrar target de todas as ações web.
- `V2-056` — migrar condições de elemento.
- `V2-057` — migrar trigger/options do Select2.
- `V2-058` — migrar ready, success e protocol.
- `V2-059` — migrar download por clique e alvos de screenshot quando aplicável.
- `V2-060` — remover acessos diretos a locators de negócio fora do resolver.
- `V2-061` — criar teste arquitetural que permita `Page.Locator`, `Frame.Locator` e `FrameLocator` somente dentro do compilador/resolver e em infraestrutura explicitamente autorizada.
- `V2-062` — executar todas as fixtures em modo strict usando apenas o candidato principal.

#### Gate V2-G5 — paridade estrita

As fixtures V2 reproduzem o comportamento observável das fixtures V1, inclusive frames, scopes, textos dinâmicos, cardinalidades e seletores auxiliares.

### Fase V2-6 — fallback manual e orçamento de resolução

Dependência: `V2-G5`.

#### Tarefas

- `V2-070` — percorrer candidatos exatos na ordem do catálogo.
- `V2-071` — classificar falhas: não encontrado, ambíguo, estado inválido, timeout, receita inválida e página/contexto encerrado.
- `V2-072` — respeitar orçamento total da ação, sem multiplicar o timeout por candidato.
- `V2-073` — interromper imediatamente em erro não recuperável.
- `V2-074` — manter ações opcionais opcionais sem mascarar erro de pacote.
- `V2-075` — emitir diagnóstico por tentativa sem expor dados sensíveis.
- `V2-076` — cobrir candidato principal válido, fallback válido, todos inválidos, ambiguidade e timeout.

#### Gate V2-G6

O modo strict usa somente o principal; o modo fallback usa alternativas exatas ordenadas; nenhum deles executa heurística.

### Fase V2-7 — fingerprints e heurística determinística

Dependência: `V2-G6`.

#### Tarefas

- `V2-080` — definir o fingerprint sanitizado e seus limites de tamanho, profundidade e atributos.
- `V2-081` — impedir captura de value sensível, senha, token, cookie, storage ou texto marcado como privado.
- `V2-082` — criar páginas-fixture com mudanças controladas de ID, classe, estrutura, texto, pai e irmãos.
- `V2-083` — fixar no harness de desenvolvimento o Scrapling `v0.4.14`, commit `5d213a2`.
- `V2-084` — produzir vetores de referência do algoritmo relevante, sem dependência Python em produção.
- `V2-085` — portar para C# os componentes selecionados de similaridade de texto, atributos, caminho, pai e irmãos.
- `V2-086` — documentar divergências intencionais em relação à referência.
- `V2-087` — aplicar limites de candidatos, nós, profundidade, atributos, texto, memória e tempo.
- `V2-088` — exigir confiança mínima, diferença mínima para o segundo colocado, cardinalidade e estado válidos.
- `V2-089` — rejeitar empate, baixa confiança e resultado ambíguo.
- `V2-090` — calibrar thresholds com fixtures; nenhum valor entra apenas por intuição.
- `V2-091` — gerar receita executável nova somente depois da aceitação segura.

#### Gate V2-G7

O mesmo DOM e fingerprint produzem o mesmo ranking; ambiguidades falham de forma segura; Python não é dependência do worker, editor ou runtime.

### Fase V2-8 — aprendizado provisório, promoção e write-back

Dependência: `V2-G7`.

#### Tarefas

- `V2-100` — criar sessão de aprendizado isolada por `executionId`.
- `V2-101` — registrar candidato aprendido como provisório, sem mutar o snapshot.
- `V2-102` — reutilizar provisoriamente o aprendizado apenas dentro da mesma execução.
- `V2-103` — descartar aprendizado em `Validated`, `Failed`, `Retry`, `Cancelled` e encerramento inesperado.
- `V2-104` — confirmar aprendizado somente após resultado final `Succeeded`.
- `V2-105` — implementar promoção move-to-front, preservando `origin`, papel, ordem original e timestamps.
- `V2-106` — mover principal que falhou para o final apenas na revisão confirmada e conforme política.
- `V2-107` — implementar `Disabled`, `Memory`, `Source` e `Overlay`.
- `V2-108` — usar compare-and-swap na confirmação e definir resolução determinística de conflito.
- `V2-109` — nunca bloquear outras execuções enquanto uma promoção aguarda persistência.
- `V2-110` — provar por teste que duas execuções do mesmo RPA não compartilham estado provisório.

#### Gate V2-G8

Aprendizado nunca vaza entre execuções, nunca é persistido antes do sucesso e nunca reescreve silenciosamente uma revisão concorrente.

### Fase V2-9 — registry do worker e provider SQL Server

Dependências: `V2-G4` e `V2-G8`.

#### Tarefas

- `V2-120` — criar `RpaPackageRuntimeRegistry` indexado por RPA, origem e revisão.
- `V2-121` — obter o snapshot antes de iniciar o executor e manter a referência até o fim.
- `V2-122` — trocar `FlowFile` por uma referência explícita de pacote na configuração final do worker.
- `V2-123` — persistir revisão e hash usados no registro de execução.
- `V2-124` — não usar TTL como fonte de consistência; revisão identifica a versão.
- `V2-125` — criar `RpaFlow.Packages.SqlServer` sem acoplar SQL aos contratos ou ao Playwright.
- `V2-126` — criar migrations para pacote atual, documentos, histórico e metadados de revisão.
- `V2-127` — implementar leitura consistente dos três documentos na mesma transação.
- `V2-128` — implementar compare-and-swap no SQL.
- `V2-129` — testar workers distintos consumindo revisões diferentes sem lockstep.
- `V2-130` — testar conflito de promoção e continuidade dos demais casos.
- `V2-131` — preservar o claim e o paralelismo já existentes no worker.

#### Gate V2-G9

Cada execução registra exatamente qual revisão usou; publicar ou aprender uma revisão não interrompe nem altera execuções em andamento.

### Fase V2-10 — editor Blockly V2

Dependências: `V2-G4`, `V2-G5` e `V2-G9`.

#### Tarefas

- `V2-140` — criar teste de caracterização antes de dividir o `app.js`.
- `V2-141` — separar boot, estado, API, catálogo de blocos, serialização, validação, toolbox, campos customizados e UI de localizadores.
- `V2-142` — manter o round-trip dos 35 blocos durante a refatoração.
- `V2-143` — mudar a sessão do editor de arquivos isolados para um pacote/revisão aberto.
- `V2-144` — expor APIs de package, flow, locators e policy com token local e revisão esperada.
- `V2-145` — usar o package store para gravação atômica; não gravar os três documentos independentemente.
- `V2-146` — implementar `FieldLocatorReference` no Blockly.
- `V2-147` — criar seletor pesquisável por ID e nome amigável.
- `V2-148` — criar popover resumido e drawer completo com candidatos, receitas, fingerprints, origem, papéis e ordem.
- `V2-149` — exibir warnings de locator ausente, não usado, cardinalidade incompatível e política insegura.
- `V2-150` — permitir editar locator sem duplicar a receita dentro do bloco.
- `V2-151` — tratar conflito de revisão com recarregar, comparar ou salvar nova revisão; nunca sobrescrever em silêncio.
- `V2-152` — testar abrir → editar → salvar → reabrir sem perda de semântica.
- `V2-153` — atualizar manual, catálogo e guia de extensão de blocos para a V2.

#### Gate V2-G10

O editor manipula os três documentos como uma unidade versionada, e nenhum bloco armazena seletor de negócio diretamente.

### Fase V2-11 — diagnósticos, artefatos, retry e hardening

Dependências: `V2-G8`, `V2-G9` e `V2-G10`.

#### Tarefas

- `V2-160` — emitir eventos de início e fim da resolução, candidato tentado, fallback, heurística, rejeição e promoção.
- `V2-161` — incluir RPA, execução, ação, locator, candidato, revisão, tempos e motivo, sem segredo.
- `V2-162` — produzir em falha screenshot, HTML sanitizado limitado e relatório de resolução.
- `V2-163` — impor retenção, tamanho máximo e redação de dados nos artefatos.
- `V2-164` — marcar falhas retryable apenas quando a causa permitir nova tentativa.
- `V2-165` — impedir retry automático depois de efeito irreversível já registrado, preservando o mecanismo existente sem criar contrato específico novo.
- `V2-166` — limitar pacotes, documentos, locators, candidatos, fingerprints e profundidade.
- `V2-167` — proteger logs, exceções e notificações contra valores, seletores sensíveis e HTML bruto.
- `V2-168` — gerar SBOM e avisos de licenças, incluindo a adaptação baseada em Scrapling.
- `V2-169` — executar análise de dependências e testes de carga da resolução.

#### Gate V2-G11

Uma falha pode ser diagnosticada sem reexecutar o caso e sem expor segredo; retry não repete efeito irreversível conhecido.

### Fase V2-12 — migrador offline V1 → V2

Dependências: `V2-G5` e `V2-G10`.

#### Tarefas

- `V2-180` — criar `tools/RpaFlow.Migrator` sem referência pelo worker V2.
- `V2-181` — manter DTOs V1 somente nesse projeto ou em assembly histórico isolado.
- `V2-182` — aceitar arquivo, diretório de RPA e modo em lote.
- `V2-183` — validar a entrada V1 antes de converter.
- `V2-184` — preservar IDs e ordem de ações, subflows, inputs e valores.
- `V2-185` — gerar IDs `{actionId}.target`, `.trigger`, `.options`, `.ready`, `.success`, `.protocol` e `.condition`.
- `V2-186` — converter selector, scope, textos literais/dinâmicos, frames e match mode.
- `V2-187` — converter todos os seletores auxiliares e download por clique.
- `V2-188` — usar `rawPlaywright` para preservar sintaxe sem reinterpretação.
- `V2-189` — não deduplicar locators automaticamente.
- `V2-190` — gerar política strict conservadora por padrão.
- `V2-191` — gerar relatório com ações, locators, coleções, usos first, casos especiais, possíveis duplicidades e revisões humanas.
- `V2-192` — suportar `--dry-run`, diretório de saída separado e backup; nunca sobrescrever a origem por padrão.
- `V2-193` — garantir saída determinística para a mesma entrada.
- `V2-194` — validar o pacote produzido e abri-lo no editor V2.
- `V2-195` — executar testes diferenciais V1/V2 sobre as páginas-fixture.

#### Gate V2-G12 — migração aprovada para o cutover

- todas as fixtures V1 possuem pacote V2 válido;
- diferenças funcionais estão zeradas ou aprovadas e documentadas;
- migrador não altera origem e produz relatório completo;
- editor abre e salva o resultado;
- runtime executa o pacote em strict;
- contratos e APIs necessárias ao Recorder estão congelados.

### Fase V2-13 — cutover do repositório e release candidate

Dependência: `V2-G12`.

#### Tarefas

- `V2-200` — migrar `examples/RpaExemplo`.
- `V2-201` — migrar `templates/rpa-web`.
- `V2-202` — adaptar scripts e documentação para abrir pacotes V2.
- `V2-203` — tornar o carregamento V2 o único caminho operacional do worker e editor.
- `V2-204` — remover dependências do runtime V1 dos projetos de produção.
- `V2-205` — manter fixtures V1 e desserializador apenas no migrador/testes históricos.
- `V2-206` — publicar runbook de migração, verificação e rollback do conteúdo mantido neste repositório.
- `V2-207` — executar suíte completa em Windows e ambiente de CI.
- `V2-208` — produzir pacote/release candidate `2.0.0-rc.1`.

#### Gate V2-G13 — V2 pronta para servir de base

Outro desenvolvedor consegue criar, editar, versionar, executar e migrar um RPA V2 usando apenas o repositório e sua documentação.

## 7. Trilha B — Recorder para Chrome

Esta trilha começa somente depois de `V2-G13`. Seus schemas Recorder podem evoluir de forma independente, mas o conteúdo de `package/` deve continuar sendo exatamente o pacote V2 oficial.

### Fase REC-1 — ADRs, threat model e contratos do bundle

Dependência: `V2-G13`.

#### Tarefas

- `REC-001` — aprovar ADRs 012 a 018 e revisar o threat model.
- `REC-002` — adicionar `origin: recorder` e `recorderRole: capturedPrimary | capturedAlternative` ao contrato oficial.
- `REC-003` — definir schemas de manifest, session, evidence e issues.
- `REC-004` — definir códigos estáveis de pendência e suas severidades.
- `REC-005` — definir limites de arquivos, entradas, compressão, evidências, texto e duração da sessão.
- `REC-006` — definir formato de integridade com SHA-256 e tamanho por entrada.
- `REC-007` — definir golden bundles mínimos, completos, inválidos e maliciosos.
- `REC-008` — gerar tipos TypeScript e validar os mesmos golden files em C# e TypeScript.

#### Gate REC-G1

Backend e TypeScript interpretam exatamente os mesmos documentos; campos desconhecidos, opcionais e limites possuem comportamento definido.

### Fase REC-2 — esqueleto Manifest V3

Dependência: `REC-G1`.

#### Tarefas

- `REC-010` — criar projeto TypeScript, lockfile, lint, testes e build reprodutível.
- `REC-011` — criar manifest MV3 com CSP restritiva e sem código remoto.
- `REC-012` — declarar `activeTab`, `scripting`, `storage`, `downloads` e `sidePanel`; hosts ficam opcionais.
- `REC-013` — implementar side panel, service worker e content script com mensagens tipadas.
- `REC-014` — implementar máquina de estados: idle, recording, paused, finalizing, completed e failed.
- `REC-015` — persistir estado não sensível e checkpoints idempotentes em `chrome.storage.session`.
- `REC-016` — recuperar a sessão depois da suspensão do service worker.
- `REC-017` — solicitar host permission somente por gesto do usuário e somente para a origem necessária.
- `REC-018` — documentar instalação unpacked e diagnóstico local.

#### Gate REC-G2

A extensão inicia, pausa, retoma e encerra uma sessão após suspensões simuladas, sem companion app e sem permissão ampla obrigatória.

### Fase REC-3 — captura e normalização determinística

Dependência: `REC-G2`.

#### Tarefas

- `REC-020` — capturar click, input, change, submit, teclas semânticas, select, navegação, tab, popup e upload.
- `REC-021` — registrar frame, aba, URL sanitizada, timestamp, sequência e relação causal.
- `REC-022` — coalescer digitação contínua em uma ação fill.
- `REC-023` — coalescer input/change e click/submit duplicados.
- `REC-024` — distinguir navegação tradicional, SPA e troca de aba/popup quando observável.
- `REC-025` — mapear apenas para tipos existentes no catálogo V2 gerado.
- `REC-026` — transformar caso não suportado em issue, nunca em ação inventada.
- `REC-027` — usar IDs determinísticos e retomada idempotente.
- `REC-028` — testar a mesma sequência várias vezes e comparar bytes normalizados.

#### Gate REC-G3

Os mesmos eventos produzem os mesmos intents; uma digitação não gera dezenas de fills; casos incertos ficam visíveis como pendência.

### Fase REC-4 — autoria de localizadores e fingerprints

Dependência: `REC-G3`.

#### Tarefas

- `REC-030` — gerar candidatos em ordem versionada: testId, role+name, label, atributos estáveis, placeholder, texto exato, ID estável, CSS curto, CSS estrutural e XPath.
- `REC-031` — validar no DOM que cada candidato resolve exatamente o elemento capturado.
- `REC-032` — aceitar como executável apenas candidato único e não sensível.
- `REC-033` — manter candidato ambíguo somente como diagnóstico.
- `REC-034` — detectar IDs/classes dinâmicos e atributos sensíveis.
- `REC-035` — representar frames e scopes pela receita V2 completa.
- `REC-036` — suportar shadow root aberto; registrar issue para shadow root fechado.
- `REC-037` — capturar fingerprint sanitizado compatível com a V2.
- `REC-038` — atribuir `origin: recorder` e papel de autoria sem promover candidato.
- `REC-039` — provar que nenhum valor secreto entra em candidato ou fingerprint.

#### Gate REC-G4

Principal e alternativas executáveis resolvem exclusivamente o alvo capturado; ranking é determinístico; Recorder não executa heurística adaptativa.

### Fase REC-5 — geração nativa do pacote V2

Dependências: `REC-G3` e `REC-G4`.

#### Tarefas

- `REC-040` — mapear intents somente para os 32 tipos suportados, conforme aplicável à captura web.
- `REC-041` — gerar ações com referências de locator, nunca seletores embutidos.
- `REC-042` — materializar `input.recorded.*` para valores não sensíveis.
- `REC-043` — materializar `secret.recorded.*` apenas como referência.
- `REC-044` — gerar `samples/inputs.sample.json` separado do fluxo.
- `REC-045` — gerar `locators.production.json` com origem e papéis preservados.
- `REC-046` — gerar `rpa.policy.json` conservador: strict, sem promoção e sem write-back.
- `REC-047` — validar os três documentos com o validator oficial V2.
- `REC-048` — bloquear finalização quando existir issue blocking sem resolução explícita.

#### Gate REC-G5

O diretório `package/` gerado pela extensão é aberto diretamente pelo editor e executado pela V2 sem conversão.

### Fase REC-6 — evidências, timeline e revisão local

Dependência: `REC-G3`.

#### Tarefas

- `REC-050` — desenhar overlay de destaque sem alterar permanentemente a página.
- `REC-051` — mascarar campos e regiões sensíveis antes da persistência.
- `REC-052` — capturar somente a área visível, respeitando o limite da API do Chrome.
- `REC-053` — impor rate limit, tamanho, resolução e quantidade de screenshots.
- `REC-054` — gerar WebP, thumbnails e `evidence/index.json`.
- `REC-055` — associar evento, intent, ação, locator e evidência por IDs estáveis.
- `REC-056` — criar timeline, slideshow estático e comentários.
- `REC-057` — permitir remover evidência sem invalidar o fluxo.
- `REC-058` — testar que slideshow nunca navega nem executa a página.

#### Gate REC-G6

O usuário revisa visualmente a gravação sem replay, e nenhuma evidência contém senha ou região marcada como sensível.

### Fase REC-7 — segredos e uploads

Dependências: `REC-G5` e `REC-G6`.

#### Tarefas

- `REC-060` — manter captura de senha desligada por padrão e por sessão.
- `REC-061` — exigir consentimento explícito e chave pública destinatária válida.
- `REC-062` — cifrar imediatamente cada segredo com AES-256-GCM.
- `REC-063` — envolver a chave simétrica com RSA-OAEP-SHA-256 e registrar apenas key ID.
- `REC-064` — apagar buffers e estado transitório na finalização, cancelamento e falha.
- `REC-065` — verificar por busca automatizada que o texto claro não aparece em storage, logs, eventos, screenshots ou ZIP.
- `REC-066` — registrar intenção de upload, nome sanitizado, MIME, tamanho e hash.
- `REC-067` — não incluir conteúdo do upload por padrão.
- `REC-068` — exigir toggle e consentimento para incluir arquivo em `samples/uploads/`.
- `REC-069` — impor allowlist/bloqueios e limites por arquivo e total.
- `REC-070` — exigir remapeamento de segredo e attachment no editor antes da publicação.

#### Gate REC-G7

Sem chave pública não há captura de senha; chave errada não revela conteúdo; upload nunca é incluído silenciosamente.

### Fase REC-8 — bundle, integridade e download

Dependências: `REC-G5`, `REC-G6` e `REC-G7`.

#### Tarefas

- `REC-080` — montar a estrutura canônica sem diretórios opcionais vazios.
- `REC-081` — gerar manifest com versões, origem, contagens, presença de segredos/uploads e declaração sem replay.
- `REC-082` — gerar `recording/session.json`, events, issues e comments.
- `REC-083` — calcular hashes e tamanhos depois da serialização final.
- `REC-084` — ordenar entradas e metadados para ZIP reproduzível.
- `REC-085` — gerar o ZIP localmente e iniciar download pela API do Chrome.
- `REC-086` — mostrar progresso e permitir cancelamento seguro.
- `REC-087` — limpar a sessão somente depois de confirmar o download ou por ação explícita.
- `REC-088` — adulterar cada classe de entrada nos testes e confirmar rejeição futura pelo importador.

#### Gate REC-G8

O mesmo conteúdo lógico gera bundle determinístico; hashes conferem; o download funciona sem serviço local.

### Fase REC-9 — backend seguro de importação

Dependências: `V2-G13` e `REC-G8`.

#### Tarefas

- `REC-090` — criar serviços de upload, segurança, contrato, staging, preview, conflitos, apply, segredos e evidências.
- `REC-091` — implementar endpoints de inspect, get, evidence, validate, apply e delete.
- `REC-092` — rejeitar caminho absoluto, `..`, separador enganoso, symlink e tipo de entrada inesperado.
- `REC-093` — rejeitar nomes duplicados sem diferença de caixa.
- `REC-094` — impor quantidade, tamanho compactado, tamanho descompactado e razão de compressão.
- `REC-095` — validar hashes antes de desserializar JSON.
- `REC-096` — usar staging isolado, aleatório, com expiração e limpeza idempotente.
- `REC-097` — nunca extrair diretamente no projeto aberto.
- `REC-098` — garantir que inspect e validate sejam somente leitura.
- `REC-099` — manter chave privada exclusivamente no backend/provedor de segredo autorizado.
- `REC-100` — registrar auditoria sem conteúdo sensível.

#### Gate REC-G9

Bundles maliciosos ou adulterados são recusados; cancelar ou falhar antes do apply não altera o pacote de destino nem deixa staging acessível.

### Fase REC-10 — wizard de importação e conflitos

Dependência: `REC-G9`.

#### Tarefas

- `REC-110` — criar fluxo selecionar → inspecionar → revisar → mapear → confirmar → aplicar.
- `REC-111` — mostrar timeline, slideshow, issues, comentários e proveniência.
- `REC-112` — comparar IDs, nomes, inputs, subflows, locators e revisões com o pacote aberto.
- `REC-113` — suportar somente três modos explícitos: substituir, acrescentar ao principal ou importar como subflow.
- `REC-114` — remapear IDs de forma determinística quando autorizado.
- `REC-115` — exigir resolução de conflitos e pendências blocking.
- `REC-116` — mapear samples para inputs reais sem publicar valores de exemplo como configuração de produção.
- `REC-117` — mapear referências de segredo e attachments.
- `REC-118` — tornar cancelamento e repetição idempotentes.

#### Gate REC-G10

Antes do apply, o usuário enxerga exatamente o que mudará; nenhum conflito é resolvido silenciosamente.

### Fase REC-11 — aplicação no Blockly e persistência

Dependências: `REC-G10` e `V2-G10`.

#### Tarefas

- `REC-120` — aplicar merge semântico em memória sobre a revisão esperada.
- `REC-121` — validar o pacote resultante antes de persistir.
- `REC-122` — salvar atomicamente pelo package store e manter revisão/backup.
- `REC-123` — converter ações aplicadas em blocos e executar auto-layout.
- `REC-124` — conectar blocos ao `FieldLocatorReference` e ao drawer V2.
- `REC-125` — preservar `origin`, `recorderRole`, receitas, ordem e associação com evidência.
- `REC-126` — recusar apply se a revisão aberta mudou desde o preview.
- `REC-127` — reabrir o pacote salvo e comparar semanticamente.

#### Gate REC-G11

Recorder → ZIP → editor → salvar → reabrir preserva ações, localizadores, política, proveniência e evidências relevantes.

### Fase REC-12 — integração ponta a ponta e release

Dependência: `REC-G11`.

#### Tarefas

- `REC-130` — criar site-fixture com formulário, SPA, iframe, popup, select, upload e alteração de DOM.
- `REC-131` — gravar e exportar o site-fixture pela extensão real.
- `REC-132` — importar, revisar e aplicar no editor real.
- `REC-133` — executar em strict pelo file store.
- `REC-134` — executar com fallback depois de alteração controlada do DOM.
- `REC-135` — confirmar snapshot imutável e concorrência independente no worker.
- `REC-136` — executar testes de determinismo, memória, desempenho, acessibilidade e suspensão do service worker.
- `REC-137` — revisar threat model, SBOM, licenças e vulnerabilidades.
- `REC-138` — produzir build reprodutível da extensão e checksum.
- `REC-139` — publicar manual do cliente, manual do desenvolvedor, privacidade e troubleshooting.
- `REC-140` — realizar teste de instalação limpa por pessoa que não participou da implementação.

#### Gate REC-G12 — produto concluído

Um usuário instala a extensão, grava um roteiro, revisa e baixa um único ZIP; um desenvolvedor importa, resolve pendências, salva e executa o pacote V2 sem editar JSON manualmente e sem recorrer à conversa original.

## 8. Estratégia de commits e integração

A branch guarda-chuva é `feature/rpablockly-v2`. A implementação deve usar commits pequenos e coerentes, sempre com a suíte anterior verde. Se houver mais de um desenvolvedor, branches curtas podem partir dela e retornar por revisão, mas o histórico final deve manter as fronteiras abaixo:

1. documentação, ADRs e baseline;
2. schemas e contratos;
3. packages e stores;
4. resolver strict;
5. fallback;
6. fingerprints e heurística;
7. aprendizado e concorrência;
8. SQL e worker;
9. editor V2;
10. diagnósticos e hardening;
11. migrador e cutover V2;
12. contratos Recorder;
13. extensão por capacidade;
14. importador por capacidade;
15. E2E, documentação e release.

Não misturar mudança de schema, refatoração ampla do editor e nova funcionalidade do Recorder no mesmo commit.

## 9. Estratégia de testes

### 9.1 Pirâmide de verificação

| Camada | O que comprova |
|---|---|
| Unitário | validações, ranking, coalescência, hashes, IDs e conflitos |
| Contrato cruzado | C# e TypeScript aceitam/rejeitam os mesmos arquivos |
| Golden/determinismo | mesma entrada produz JSON, intents e bundle equivalentes |
| Integração | package stores, SQL, editor, staging e worker |
| Playwright fixture | receita, cardinalidade, fallback, heurística e evidências |
| Concorrência | snapshots independentes e compare-and-swap |
| Segurança | segredos, ZIP malicioso, limites, sanitização e permissões |
| E2E | gravação real → ZIP → editor → store → runtime |

### 9.2 Matriz mínima de cenários V2

- principal válido;
- fallback válido depois de falha do principal;
- todos os candidatos ausentes;
- locator ambíguo;
- cardinalidade single/first/many;
- nested frames e scope com texto literal/dinâmico;
- condition, Select2, ready, success, protocol e download;
- heurística abaixo do threshold, empate, gap insuficiente e aceite válido;
- aprendizado seguido de Succeeded, Validated, Failed, Retry e Cancelled;
- duas execuções simultâneas da mesma revisão;
- publicação concorrente e conflito de CAS;
- gravação interrompida entre documentos;
- round-trip do editor com os 35 blocos.

### 9.3 Matriz mínima de cenários Recorder

- click simples e duplo;
- digitação longa, colagem e alteração final;
- checkbox, radio, select nativo e widget não suportado;
- Enter que submete e click seguido de submit;
- navegação tradicional, SPA, nova aba, popup e iframe;
- frame cross-origin sem permissão;
- shadow root aberto e fechado;
- candidato único, ambíguo, instável e sensível;
- senha sem consentimento, com chave inválida e com chave válida;
- upload omitido, incluído, grande demais e tipo bloqueado;
- suspensão do service worker em cada estado;
- bundle adulterado, Zip Slip, Zip Bomb, duplicidade de nome e symlink;
- conflito de revisão durante preview/apply;
- substituir, acrescentar e importar como subflow;
- gravação e bundle reproduzíveis.

### 9.4 Comandos de aceitação da base .NET

```powershell
dotnet restore RpaBlockly.slnx
dotnet build RpaBlockly.slnx --configuration Release --no-restore
dotnet run --project tests/RpaBase.Checks/RpaBase.Checks.csproj --configuration Release
dotnet run --project tests/RpaFlow.EditorRoundTrip/RpaFlow.EditorRoundTrip.csproj --configuration Release
dotnet run --project tests/RpaFlow.PlaywrightChecks/RpaFlow.PlaywrightChecks.csproj --configuration Release
dotnet run --project tests/Rpa.WorkerChecks/Rpa.WorkerChecks.csproj --configuration Release
```

Quando os novos projetos surgirem, a CI deve acrescentar os checks de contratos, packages, importador, TypeScript e E2E sem retirar os comandos existentes.

## 10. Segurança e privacidade como critérios de bloqueio

Uma entrega não pode avançar de gate se:

- houver segredo, token, senha, cookie, storage autenticado ou string de conexão real no repositório;
- fluxo, locator, fingerprint, log, screenshot ou bundle contiver senha em texto claro;
- um ZIP puder escrever fora do staging;
- inspeção ou preview alterar o pacote aberto;
- o frontend puder acessar chave privada;
- a extensão exigir acesso permanente a todos os sites sem justificativa aprovada;
- um resultado heurístico ambíguo puder virar clique;
- o aprendizado de uma execução puder influenciar outra antes de Succeeded;
- uma revisão puder ser sobrescrita sem compare-and-swap;
- retry puder repetir efeito irreversível já conhecido.

## 11. Migração interna e rollback da V2

Esta etapa cobre somente fixtures, exemplos, templates e documentação mantidos no próprio `Base-RPA-Blockly`. Nenhum projeto externo participa dos requisitos, testes ou critérios de aceite.

Procedimento interno:

1. fixar o commit V1 usado como baseline;
2. manter cópia imutável das fixtures V1 sanitizadas;
3. executar o migrador em `--dry-run` sobre o exemplo e o template do repositório;
4. revisar o relatório e possíveis duplicidades semânticas;
5. gerar pacotes V2 em diretórios novos;
6. abrir os pacotes no editor V2 e resolver warnings;
7. executar os testes diferenciais V1/V2 nas páginas-fixture;
8. tornar os pacotes V2 o padrão do exemplo e do template;
9. executar a suíte completa;
10. gerar a release candidate da V2;
11. validar rollback para o commit/release anterior do próprio repositório.

O rollback é feito pela versão do repositório e dos seus artefatos, não por conversão reversa automática de V2 para V1.

## 12. Dependências externas verificadas

- O side panel é uma API Manifest V3 disponível desde Chrome 114, e a abertura programática a partir de gesto do usuário está disponível desde Chrome 116: [documentação oficial do Side Panel](https://developer.chrome.com/docs/extensions/reference/api/sidePanel).
- Service workers de extensão podem ser encerrados após inatividade; estado importante não deve depender de variáveis globais: [ciclo de vida oficial](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle).
- `chrome.storage.session` é adequado ao estado da sessão, possui limite próprio e não é exposto a content scripts por padrão: [documentação oficial de storage](https://developer.chrome.com/docs/extensions/reference/api/storage).
- Host permissions opcionais podem ser solicitadas em tempo de execução por gesto do usuário: [documentação oficial de permissions](https://developer.chrome.com/docs/extensions/reference/api/permissions).
- `captureVisibleTab` exige `activeTab` ou acesso amplo e tem limite de chamadas; o plano adota `activeTab` e rate limit: [documentação oficial de tabs](https://developer.chrome.com/docs/extensions/reference/api/tabs#method-captureVisibleTab).
- A priorização de role, label e outros contratos voltados ao usuário acompanha a recomendação oficial do Playwright: [documentação oficial de locators](https://playwright.dev/docs/locators).
- A referência de heurística está fixada no [Scrapling v0.4.14](https://github.com/D4Vinci/Scrapling/tree/v0.4.14), commit `5d213a2`, sob licença BSD-3-Clause. Ela serve para harness e atribuição; produção continua nativa em C#.

## 13. Definição final de pronto

A iniciativa inteira estará concluída quando todos os itens abaixo forem verdadeiros:

- os três schemas operacionais estão versionados e validados em C# e TypeScript;
- os 32 tipos de ação e 35 blocos do baseline continuam cobertos;
- nenhuma ação V2 contém seletor de negócio embutido;
- toda localização passa pelo resolver central;
- snapshots são imutáveis e revisionados;
- concorrência é independente, sem barreira por RPA;
- aprendizagem só persiste após Succeeded;
- arquivo e SQL usam gravação consistente e compare-and-swap;
- editor V2 abre, altera e salva pacote completo sem perda;
- migrador cobre mecanicamente todos os campos de localização V1;
- exemplos e templates do repositório usam V2;
- runtime e worker de produção não desserializam V1;
- extensão produz pacote V2 nativo e determinístico;
- segredos são opcionais, cifrados imediatamente e nunca vazam;
- evidências são sanitizadas e o slideshow não executa a página;
- importador resiste a ZIP malicioso e usa staging isolado;
- preview não altera estado e apply é atômico e revisionado;
- E2E Recorder → ZIP → Editor → Store → Worker → Runtime está verde;
- documentação permite adoção sem conhecimento tácito;
- não há conteúdo ou requisito proveniente de projetos externos nem credenciais reais.
