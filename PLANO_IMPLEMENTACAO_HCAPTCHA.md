# Plano de implementação — hCaptcha (hcaptcha-challenger) no RpaBlockly

> Documento de handoff. Descreve o objetivo, o que já foi feito, o que falta,
> os contratos/decisões e os comandos de verificação. Escrito para permitir que
> outro agente/modelo continue o trabalho sem re-descobrir o contexto.
> Branch de trabalho: `captcha-solving`.
> Status: implementação concluída; o roteiro da seção 4 foi mantido como
> referência de sincronização e testes.

## 1. Objetivo

Tornar o RpaBlockly capaz de resolver desafios **hCaptcha** (além dos já
implementados: OCR de imagem, reCAPTCHA v2 por áudio, slider, Cloudflare
same-page/sidecar e fallback VLM/humano), usando os **modelos ONNX do projeto
QIN2DIM/hcaptcha-challenger** e seguindo **rigorosamente** a arquitetura já
existente:

- O **serviço Python** `services/captcha-solver` (FastAPI) faz inferência em
  CPU, isolada do worker, sem downloads em runtime (modelos fixados por
  SHA-256/tamanho em manifesto versionado e provisionados pelo usuário/CI).
- O **.NET (Playwright)** é o único componente que interage com a página
  (cliques, preenchimento, verificação de pós-condição). O serviço nunca toca
  no navegador.
- `Solved` só é declarado por pós-condição observável; intervenção humana
  (`waitHumanInput`) continua sendo o fallback final.

Escopo desta entrega (v1): desafios **image_label_binary** (grade de tiles
"clique em cada imagem que contém X"). `image_label_area_select` (point),
bounding box, drag e múltipla escolha **não** entram nesta v1 — continuam
cobertos pelo fallback VLM (`allowVlmFallback`) ou handoff humano. O manifesto
e o código foram desenhados para aceitar novos labels depois (basta adicionar
entradas no manifesto via `tools/Update-HcaptchaModelManifest.py`).

## 2. Licença dos modelos (decisão registrada)

- O repositório upstream declara GPL-3.0 para o **código-fonte**. O usuário
  (mantenedor do RpaBlockly) informou que o autor do upstream (QIN2DIM)
  autorizou **verbalmente o uso dos modelos como CC0**, e orientou tratar como
  Creative Commons Zero.
- Medidas tomadas:
  - `captcha-models/hcaptcha/LICENSE` criado com o texto canônico da CC0 1.0
    + nota de atribuição/permissão (cópia local provisionada; o diretório
    `captcha-models/` é gitignored).
  - O manifesto `hcaptcha-models.manifest.json` registra
    `"license": {"spdx": "CC0-1.0", ...}` com nota explicando a autorização e
    que os ONNX são dados de runtime (nenhum código upstream é incorporado).
  - **Nenhum código do hcaptcha-challenger foi copiado**: toda a inferência
    foi reescrita (pré-processamento PIL/numpy + onnxruntime já usados pelo
    serviço). Apenas fatos de interface do modelo foram usados (entrada
    `float32[1,3,64,64]`, escala 1/255, RGB, saída `float32[1,2]`, índice 0 =
    positivo), validados em runtime contra o manifesto.
- **Concluído**: a mesma nota foi registrada em `THIRD-PARTY-NOTICES.md` (raiz).

## 3. O que já está pronto (não refazer)

### 3.1 Manifesto e provisionamento de modelos

- `src/RpaFlow.Playwright/V2/Captcha/hcaptcha-models.manifest.json`
  **gerado e commitável**: 34 classificadores binários ResNet curados
  (airplane, bicycle, bird, boat, car, cat, dog, elephant, motorcycle, train,
  truck, mountain, robot, panda, fox, owl, goose, helicopter, castle, trees,
  sheep, snowman, motor_vehicle, flower, bridge, parrot, seaplane, tractor,
  desert, forest, beach, butterfly, ocean, hummingbird), cada um com
  `artifactUrl` (release `model` do upstream), `modelSha256`,
  `modelSizeBytes` e `aliases` (en/zh extraídos de `objects2022.yaml` +
  `objects2023.yaml` do upstream, pt-BR editoriais). Bloco `defaults` fixa o
  contrato de tensores/pré-processamento. Suite: `hcaptcha-resnet-binary`.
- `tools/Update-HcaptchaModelManifest.py` (novo): regenera o manifesto
  (baixa modelos, valida contrato ONNX via onnxruntime, recalcula hashes).
  `--verify-only` valida o manifesto sem baixar nada.
- `tools/Get-CaptchaModels.ps1`: agora tem `-Suite ocr|hcaptcha` (padrão
  `ocr`, comportamento original preservado). Na suíte hcaptcha faz download
  direto do asset (permite o 302 do GitHub; integridade garantida pelo SHA-256
  fixado). `-ModelId <id>` provisiona um só; padrão `*` provisiona todos.
  Testado: 34/34 `model_ready` e `cache_valid` idempotente (OCR também
  continua `cache_valid`).
- `services/captcha-solver/provision_models.py`: novos args
  `--hcaptcha-manifest` / `--hcaptcha-destination` (função
  `provision_hcaptcha`, redirect permitido somente para
  `objects.githubusercontent.com` e `release-assets.githubusercontent.com`).
  Testado com `--verify-only`.
- `services/captcha-solver/Dockerfile`: copia o manifesto hcaptcha e
  provisiona em `/app/models/hcaptcha` no build; exporta
  `CAPTCHA_HCAPTCHA_MODEL_DIR` e `CAPTCHA_HCAPTCHA_MANIFEST_PATH`.
- Modelos já provisionados localmente em `captcha-models/hcaptcha/*.onnx`
  (34 arquivos ~303 KB cada) + `LICENSE` CC0.

### 3.2 Serviço Python (`services/captcha-solver`) — código

- `captcha_solver/hcaptcha.py` (novo): `HCaptchaBinarySolver` com
  - normalização de prompt (NFKC, homóglifos cirílico/grego→latim, lowercase,
    colapso de espaços, strip de ponto final);
  - casamento de alias por borda de palavra (regex) para idiomas latinos e
    substring para aliases CJK; escolhe o alias mais longo; sem match →
    `SolverError("model_missing", ..., 503, details={"suggestion": "human_handoff"})`;
  - carregamento preguiçoso de sessão ONNX por label com lock, validando
    arquivo (tamanho + SHA-256) e tensores contra `defaults` do manifesto;
  - `_preprocess`: PIL decode (limita pixels), RGB (composita alpha em branco),
    resize LANCZOS 64×64, `/255`, CHW e batch fixo `[1,3,64,64]`;
  - inferência individual por tile; decisão: `match = argmax == positiveIndex(0)`,
    `confidence = sigmoid(logit vencedor)`;
  - `capability()` e `check_readiness()` (falha se manifesto/modelos ausentes).
- `captcha_solver/config.py`: novos settings `enable_hcaptcha`
  (`CAPTCHA_ENABLE_HCAPTCHA`, padrão True), `hcaptcha_model_dir`
  (`CAPTCHA_HCAPTCHA_MODEL_DIR`), `hcaptcha_manifest_path`
  (`CAPTCHA_HCAPTCHA_MANIFEST_PATH`), `max_hcaptcha_tiles`
  (`CAPTCHA_MAX_HCAPTCHA_TILES`, padrão 16, teto 64) + validações.
- `captcha_solver/schemas.py`: `SolveAssets.tilesBase64` (≤64); novo tipo de
  requisição `hcaptcha_image_label` (exige `tilesBase64` + `hint` com o
  prompt; proíbe image/audioBase64 e `task`); `TileDecision`
  `{index, match, confidence}`; `InferenceResult.tiles`.
- `captcha_solver/registry.py`: `_hcaptcha_gate` (BoundedSemaphore(1)),
  `solve_hcaptcha(tiles, hint)` (`busy` 429 retryable quando ocupado),
  capability `hcaptcha_image_label` (solver `hcaptcha-resnet-onnx`, labels,
  maxTiles), readiness com check dedicado.
- `captcha_solver/main.py`: `SERVICE_VERSION = "0.4.0"`; dispatch do tipo
  `hcaptcha_image_label` (decodifica cada tile com limite individual);
  `_enabled_types` inclui o tipo quando habilitado; `_v2_success`/`_v2_error`
  passam a incluir o campo `tiles` (null quando não aplicável).

## 4. Checklist executado (mantido como referência)

### 4.1 Testes do serviço Python

- Criar `services/captcha-solver/tests/test_hcaptcha.py`:
  - contrato HTTP via `fastapi.testclient.TestClient` seguindo o padrão de
    `tests/test_contract.py` (classe `FakeRegistry` — adicionar método
    `solve_hcaptcha(tiles, hint)` retornando `InferenceResult(tiles=[...])`):
    envelope V2 correlacionado, campo `tiles`, erros (`unsupported_type` 422
    quando `CAPTCHA_ENABLE_HCAPTCHA=0`, `invalid_payload` sem tiles/sem hint,
    `busy` 429 com Retry-After), `attempts` dentro do orçamento;
  - unit tests de `_normalize_text`/`_alias_matches` (homóglifos, borda de
    palavra, CJK, alias mais longo vence);
  - smoke real com `pytest.mark.skipif(os.environ.get("RPABLOCKLY_REQUIRE_CAPTCHA_MODELS") != "1")`
    (padrão de `tests/test_models.py`): carrega `airplane2310.onnx`
    provisionado, infere tiles sintéticos 128×128 (PIL), asserta 1 decisão por
    tile, `index` sequencial e `0 <= confidence <= 1`.
- Rodar: `& C:\Users\Rodrigo\AppData\Local\Temp\cybervinci\captcha-venv\Scripts\python.exe -m pytest services/captcha-solver/tests -q`
  (venv já existe com requirements.lock + pyyaml + pytest).
- Atualizar `services/captcha-solver/README.md`: linha do contrato na tabela,
  variáveis `CAPTCHA_ENABLE_HCAPTCHA`, `CAPTCHA_HCAPTCHA_MODEL_DIR`,
  `CAPTCHA_HCAPTCHA_MANIFEST_PATH`, `CAPTCHA_MAX_HCAPTCHA_TILES`,
  provisionamento (`-Suite hcaptcha`), licença CC0 dos modelos e limite de
  escopo (somente grade binária; point/drag continuam VLM/humano).
- CI (`.github/workflows/ci.yml`, job `captcha-service`): acrescentar
  `--hcaptcha-manifest ... --hcaptcha-destination captcha-models/hcaptcha` ao
  passo "Provisionar modelo OCR fixado" e exportar
  `CAPTCHA_HCAPTCHA_MODEL_DIR`/`CAPTCHA_HCAPTCHA_MANIFEST_PATH` nos passos de
  teste (mesmo padrão das variáveis OCR).

### 4.2 Contrato .NET (`src/RpaFlow.Playwright/V2/Captcha/`)

- `CaptchaContracts.cs`: novo record
  `CaptchaTileDecision(int Index, bool Match, double? Confidence)` e nova
  propriedade `IReadOnlyList<CaptchaTileDecision>? TileDecisions` em
  `CaptchaSolveResult` (serialização já usa camelCase; campo extra é
  retrocompatível).
- `CaptchaServiceClient.cs`:
  - ler `tiles` do envelope quando presente; validar: array 1..64, `index`
    inteiro 0..63 único, `match` bool, `confidence` 0..1 ou ausente; quando a
    requisição tinha `assets.tilesBase64`, exigir `tiles.Length` igual à
    quantidade enviada (defesa contra resposta de outro desafio);
  - hoje `ReadV2Result` exige `actions` não vazio — relaxar: aceitar resposta
    sem `actions` **somente** quando `tiles` válido estiver presente;
  - `ServiceTypeToKind`: mapear `"hcaptcha_image_label"` →
    `CaptchaKind.HCaptcha`.

### 4.3 Solver .NET — novo `HCaptchaSolver.cs` (espelhar `RecaptchaV2Solver.cs`)

Fluxo (tudo via Playwright; serviço só classifica):

1. `anchorFrame = page.FrameLocator("iframe[src*='hcaptcha'][src*='checkbox'], iframe[src*='hcaptcha'][src*='anchor']")`;
   se `#checkbox[aria-checked='true']` (ou `#checkbox` com classe de
   concluído) → retorna solved sem custo.
2. Clicar `#checkbox`; aguardar o frame de desafio
   `iframe[src*='hcaptcha'][src*='challenge']` aparecer (wait ~5 s; se o
   checkbox ficar resolvido direto, retorna solved).
3. Loop até `ResolveMaximumAttempts` (usar nova opção
   `CaptchaOptions.HCaptchaMaxAttempts`, padrão 3, validador 1..10):
   - `prompt = challengeFrame.Locator(".prompt-text").InnerTextAsync()`;
   - `tiles = challengeFrame.Locator(".task-grid .image")`; se count == 0 →
     não é grade binária → retornar outcome `Unsupported` (o chamador decide
     VLM/humano; ver 4.4);
   - screenshot PNG de cada tile (máx. 16; se mais, Unsupported);
   - `snapshotId = SHA256(concat(bytes dos tiles))` em hex (mesmo padrão do
     reCAPTCHA);
   - chamar `CaptchaServiceClient.SolveV2Async` com
     `Type = "hcaptcha_image_label"`, `Assets = { tilesBase64 = [...] }`,
     `Hint = prompt`, `Geometry = { ["tileCount"] = n }`,
     `Options = { maxAttempts = restante, localOnly = true, allowVlmFallback = false }`,
     `Provider = "hcaptcha"`, `Kind = CaptchaKind.HCaptcha`;
     compor tentativas com `V2CaptchaActionHandler.ComposeAttempts`;
   - clicar `tiles.Nth(i)` para cada decisão `match == true` (Delay ~40-80 ms);
   - clicar `challengeFrame.Locator(".button-submit")` (rótulos
     Verify/Next/Skip/Submit variam por idioma; o seletor de classe é estável);
   - aguardar transição: checkbox resolvido (`aria-checked`), frame de
     desafio desaparecer, ou nova rodada (tiles substituídos) — checar com
     `WaitForAsync` curto (~4 s) como faz `RecaptchaV2Solver.IsSolvedAsync`;
     `.display-error` visível conta como tentativa falha e segue o loop.
4. Retornar outcome `(Solved, Attempts, Unsupported)`.
Restrições de segurança: não navegar fora dos frames oficiais
(`hcaptcha.com`, `newassets.hcaptcha.com`); cada tile é limitado pelo serviço
(5 MiB); nenhum cookie/token entra em log.

### 4.4 Handler .NET — `V2CaptchaActionHandler.cs`

- `SupportedTypes` += `"solveHCaptcha"`; `switch` += case
  `"solvehcaptcha"` → `SolveHCaptchaAsync`; `KindForAction` +=
  `"solvehcaptcha" => CaptchaKind.HCaptcha`.
- `SolveHCaptchaAsync` (espelha `SolveRecaptchaAsync`): exige
  `Captcha.ServiceUrl` (mensagem orientando o serviço Python); chama
  `HCaptchaSolver.ExecuteAsync`; escreve `CaptchaSolveResult`
  (`SolverId = "hcaptcha-resnet-onnx"`, `VerificationEvidence` com o seletor
  do checkbox quando resolvido); desafio não-binário → falha
  `UnsupportedType` orientando `solveCaptcha` para fallback VLM.
- Roteamento em `SolveAutoAsync` (hoje: HCaptcha ≠ "challenge" → handoff;
  = "challenge" → `SolveVisualOrHumanAsync`):
  - qualquer variante HCaptcha → se `options.ServiceUrl` configurado, tenta
    `HCaptchaSolver` primeiro;
  - outcome `Unsupported` (não é grade binária) **ou** `CaptchaException`
    com `ModelMissing`/`UnsupportedType` (label ou capacidade indisponível) →
    cai para `SolveVisualOrHumanAsync` (VLM se `AllowVlmFallback`, senão
    handoff);
  - demais falhas são finais (mesma disciplina do reCAPTCHA);
  - sem `ServiceUrl` → comportamento atual (VLM/humano) inalterado.
- `CaptchaOptions.cs`: adicionar `int HCaptchaMaxAttempts = 3`;
  `Core/PlaywrightRuntimeOptionsValidator.cs`: validar 1..10;
  refletir a opção nos `appsettings.example.json` de `src/Rpa.Worker`,
  `examples/RpaExemplo` e `templates/rpa-web` (seção `Captcha`, manter ordem).

### 4.5 Sincronização de catálogo (obrigatória; 38 → 39 tipos)

Guia canônico: `docs/referencia-markdown/como-adicionar-bloco.md`.
Pontos a alterar (todos já localizados):

1. `src/RpaFlow.Contracts/Flow/FlowActionCatalog.cs`:
   `CapabilitiesByType["solveHCaptcha"] = [FlowCapabilities.Web]` e
   `V2OnlyTypes` (passa a 6 ações).
2. `schemas/flow-v2.schema.json`: enum `action.type` (linha ~137) e o enum da
   regra `allOf` de `captcha` (linha ~248) += `"solveHCaptcha"`.
3. `src/RpaFlow.Contracts/V2/FlowDefinitionValidator.cs`:
   `CaptchaConfigurationActionTypes` += `"solveHCaptcha"`.
4. Regenerar TypeScript: `dotnet run --project tools/RpaFlow.ContractGenerator`
   (atualiza `schemas/generated/contracts.ts`; não editar à mão — o hash
   `schemas-sha256` do cabeçalho é validado em check).
5. `src/RpaFlow.Editor/wwwroot/v2/action-catalog.js`:
   `entry("solveHCaptcha", "rpa_solve_hcaptcha", "Resolver hCaptcha", "Captchas", [])`
   (os blocos Blockly são gerados dinamicamente do catálogo; toolbox.js e
   validation.js não precisam de mudança específica).
6. `src/RpaFlow.Playwright/Flow/DataAndArtifactActionHandler.cs` (handler V1):
   `SupportedTypes` += `"solveHCaptcha"` (mantém a mensagem de "só V2"
   coerente para fluxos V1).
7. `tests/RpaFlow.ContractsChecks/Program.cs`: `expectedTypes` +=
   `"solveHCaptcha"` e ajustar a mensagem "38 tipos" → "39 tipos"; conjunto
   `V2OnlyTypes` += `"solveHCaptcha"` ("cinco" → "seis").
8. `tests/RpaFlow.MigratorChecks/Program.cs`: família `["captcha"]` +=
   `"solveHCaptcha"`.
9. `docs/referencia-markdown/catalogo-de-blocos.md`: linha na tabela de
   captchas (`rpa_solve_hcaptcha` | `solveHCaptcha` | — | Resolve a grade
   binária pelo serviço opcional.).
10. `README.md` (raiz): incluir `solveHCaptcha` na seção de captchas.
11. `docs/referencia-markdown/como-adicionar-bloco.md`: atualizar "38 tipos"
    para "39 tipos".

### 4.6 Testes .NET (`tests/RpaFlow.PlaywrightChecks/Program.cs`)

- Cliente HTTP: seguir o padrão `CheckCaptchaServiceClientAsync` com
  `HttpListener` local — respostas: sucesso com `tiles` (asserta
  `TileDecisions`), `tiles` com índice duplicado/fora da faixa (espera
  `ContractViolation`), contagem de tiles divergente da requisição, erro V2
  correlacionado (`requestId/challengeId/snapshotId`).
- Fluxo de página: usar `page.RouteAsync` (padrão `CheckCloudflareSidecarAsync`)
  para servir HTML falso em `https://newassets.hcaptcha.com/...` — iframe de
  checkbox (`#checkbox` com `aria-checked`) + iframe de desafio com
  `.prompt-text`, 9 `.task-grid .image` (divs com fundo colorido gerado por
  data-URL) e `.button-submit`; serviço stub via `HttpListener` devolvendo
  `tiles` com 2 matches; assertar cliques aplicados nos tiles certos, submit
  acionado e resultado `Solved` após o checkbox virar `aria-checked='true'`
  (o HTML do checkbox pode trocar o atributo via JS ao "resolver").
- Registrar as novas chamadas de check na `Main` (seguir o estilo dos
  `Check...` existentes) e atualizar a contagem/mensagens se houver.

### 4.7 Docs finais

- `docs/adr/021-hcaptcha-onnx.md` (espelhar estrutura de
  `docs/adr/020-sidecar-cloudflare.md`: Contexto/Decisão/Segurança/
  Alternativas recusadas/Consequências/Rollback/Testes) + indexar em
  `docs/adr/README.md`. Pontos a registrar: inferência local via model hub
  CC0-autorizado; somente grade binária na v1; sem downloads em runtime;
  redirect 302 do GitHub aceito com SHA-256 fixado; denoise NLM de tiles
  144px com marca d'água **não** portado (opencv fora do escopo de deps —
  registrar como limitação conhecida; fallback VLM/humano cobre).
- `THIRD-PARTY-NOTICES.md`: entrada hcaptcha-challenger (modelos ONNX, CC0
  1.0 conforme autorização do autor, URL do repo/release).
- `services/captcha-solver/README.md` (ver 4.1) e `README.md` raiz.
- Atualizar `AGENTS.md`? Não — nada do que está documentado lá mudou.

### 4.8 Verificação final

1. `dotnet build RpaBlockly.slnx --configuration Release` (restaurar antes se
   preciso).
2. `dotnet run --project tools/RpaFlow.ContractGenerator` (após editar o
   schema) e conferir diff de `schemas/generated/contracts.ts`.
3. `pytest` do serviço (comando em 4.1).
4. Checks .NET: `./tools/Run-Checks.ps1` (roda Contracts/Packages/Migrator/
   Playwright checks + editor round-trip; PlaywrightChecks precisa do
   Chromium — `pwsh tests/RpaFlow.PlaywrightChecks/bin/Release/net9.0/playwright.ps1 install chromium`
   se faltar).
5. `pwsh tools/Get-CaptchaModels.ps1 -Suite hcaptcha -VerifyOnly` e
   `python tools/Update-HcaptchaModelManifest.py --verify-only`.

Resultado em 2026-09-06:

- `pytest services/captcha-solver/tests -q` com modelos reais: 50 testes
  passaram;
- build da solução e do template em Release: 0 erros e 0 avisos;
- `tools/Run-Checks.ps1`: passou integralmente, incluindo conformidade
  TypeScript, Recorder, 39 tipos, 42 blocos, Playwright e EditorRoundTrip;
- manifesto e 34 modelos hCaptcha passaram nos dois modos `--verify-only`;
- o checksum reproduzível do Recorder foi regenerado porque a extensão embute
  `flow-v2.schema.json`, alterado pela nova ação;
- limitação local: `global.json` solicita SDK 10.0.302, ausente nesta máquina;
  os comandos .NET foram executados com o arquivo temporariamente afastado e
  SDK 10.0.400, sempre restaurando-o no `finally`.

## 5. Armadilhas já encontradas (não tropeçar de novo)

- **UTF-8/AGENTS.md**: todos os arquivos de texto em UTF-8 sem BOM, sem
  entidades HTML nem mojibake (`ã`, `ç`, `á` literais). No console Windows,
  use `python -X utf8` ao imprimir acentos.
- **GitHub Releases**: assets respondem 302 para
  `objects.githubusercontent.com` ou `release-assets.githubusercontent.com` —
  o provisionador OCR original proíbe redirect; na suíte hcaptcha esses hosts
  foram permitidos e o SHA-256 fixado dá a garantia de integridade.
- **`contracts.ts` é gerado** — nunca editar manualmente; o check compara o
  `schemas-sha256` do cabeçalho.
- **Contagem do catálogo**: os checks validam a cardinalidade exata de 39 tipos
  em `ContractsChecks`, 42 blocos no editor e a família captcha do
  `MigratorChecks`.
- **Rate limit**: a ferramenta de subagente (`task`) falhou com "max RPM: 3"
  nesta sessão — preferir grep/read diretos.
- **Venv do serviço** já existe em
  `C:\Users\Rodrigo\AppData\Local\Temp\cybervinci\captcha-venv` (requirements.lock
  + pyyaml + pytest). Recriar se a máquina mudar.
- **Não versionar** `captcha-models/` (gitignored) nem segredos; o manifesto
  sim é versionado.
- Envelope V2 **não é reenviado** pelo cliente .NET (sem deduplicação
  server-side) — preservar essa disciplina nas mudanças do cliente.

## 6. Referências-chave

- Upstream: https://github.com/QIN2DIM/hcaptcha-challenger (model hub:
  release `model`; aliases: `archive/src/objects2022.yaml`,
  `archive/src/objects2023.yaml`).
- Contrato dos classificadores: entrada `float32[1,3,64,64]` (RGB, /255),
  saída `float32[1,2]`, índice 0 = positivo, confiança = sigmoid do logit
  vencedor. Validado em runtime contra `defaults` do manifesto.
- DOM hCaptcha usado pelo solver: checkbox `#checkbox[aria-checked]` no frame
  `hcaptcha[checkbox|anchor]`; desafio no frame `hcaptcha[challenge]` com
  `.prompt-text`, `.task-grid .image`, `.button-submit`, `.display-error`.
- Arquivos âncora da arquitetura: `docs/adr/020-sidecar-cloudflare.md`,
  `docs/referencia-markdown/como-adicionar-bloco.md`,
  `services/captcha-solver/README.md`,
  `src/RpaFlow.Playwright/V2/Captcha/{CaptchaContracts,CaptchaServiceClient,RecaptchaV2Solver,V2CaptchaActionHandler,VisualCaptchaAdapter}.cs`.
