# RpaBlockly V2

Base genérica para criar, editar, versionar e executar RPAs web em .NET 9 com
Blockly e Playwright. A V2 separa o roteiro, o catálogo de localizadores e a
política de resiliência em um pacote atômico revisionado.

O runtime operacional aceita somente schema 2. Fluxos schema 1 permanecem em um
assembly histórico isolado, exclusivamente para migração offline e testes
diferenciais.

## Como a V2 funciona

Uma execução segue este caminho:

1. o host ou worker resolve `rpaId`, origem e revisão;
2. o package store carrega uma revisão imutável contendo três documentos;
3. a validação cruza ações, locator IDs, cardinalidades, subfluxos e policy;
4. o worker fixa revisão e hash antes da primeira ação;
5. o `LocatorResolver` tenta candidatos conforme `strict`, `fallback` ou
   `adaptive`;
6. o executor produz `runtime.*`, eventos e artefatos limitados;
7. aprendizado heurístico só pode ser confirmado depois de `Succeeded` e usa
   compare-and-swap.

Cada revisão do pacote contém:

| Documento | Responsabilidade |
| --- | --- |
| `flow.production.json` | Ações schema 2, inputs, condições, loops, subfluxos e referências `locatorId`. |
| `locators.production.json` | Candidatos ordenados, receitas, frames, scope e fingerprints. |
| `rpa.policy.json` | Modo de resolução, limites, promoção e write-back. |

Seletores de negócio não ficam nas ações nem nos blocos Blockly.

## Pré-requisitos

- .NET SDK `10.0.302` ou feature band `10.0.x` posterior, conforme `global.json`;
- PowerShell 7.2 ou posterior para scripts e launcher;
- Git para validar que configurações e chaves locais não serão versionadas;
- Node.js 24 e npm para conformidade TypeScript dos schemas;
- Chromium do Playwright para o SpyBrowser padrão e os checks de navegador;
- SQL Server apenas para o worker/store SQL; os checks locais normais não iniciam
  Docker.

```powershell
dotnet restore RpaBlockly.slnx
dotnet build RpaBlockly.slnx --configuration Release
pwsh src/RpaFlow.Playwright/bin/Release/net9.0/playwright.ps1 install chromium
```

## Início rápido

O launcher prepara a configuração local, compila, instala o browser e abre o
editor. Sem argumentos, mostra um menu:

```powershell
.\rpablockly.cmd
```

Uso não interativo:

```powershell
.\rpablockly.cmd doctor
.\rpablockly.cmd setup
.\rpablockly.cmd editor
```

O setup padrão evita downloads de modelos e imagens Docker. Para preparar
captchas, escolha explicitamente um modo:

```powershell
.\rpablockly.cmd setup -CaptchaMode Models
.\rpablockly.cmd setup -CaptchaMode Docker
```

`Models` ocupa aproximadamente 25 MB no cache local. `Docker` constrói o solver
com OCR, hCaptcha e Whisper e pode ocupar 1 GB ou mais. Comandos, lifecycle e
limpeza estão no [guia do launcher](docs/referencia-markdown/launcher-local.md).

## Criar e editar um RPA

```powershell
.\tools\Novo-Rpa.ps1 `
  -Name RpaContasPagar `
  -DisplayName "Contas a pagar"

dotnet run --project rpas/RpaContasPagar/RpaContasPagar.csproj -- --validate-only
.\abrir-editor.cmd rpas\RpaContasPagar
```

O scaffold copia `templates/rpa-web`, cria `appsettings.local.json` ignorado pelo
Git e adiciona o projeto à solução. O package store inicial permanece com o ID
`rpa-template`; altere `Runtime.RpaId` e `rpa.editor.json` juntos se quiser outro
ID e publique o pacote sob esse ID.

No editor, os 42 blocos cobrem os 39 tipos de ação. O pacote é aberto por revisão;
salvar publica os três documentos atomicamente. Conflito de revisão nunca
sobrescreve alterações silenciosamente.

O browser operacional padrão é `spybrowser`, com interações humanizadas. Defina
`Runtime.SpyBrowserHumanize=false` para desativar essa cadência ou selecione
explicitamente `chromium`, Firefox, WebKit, um canal Chrome/Edge ou
`cloakbrowser`; as opções anteriores continuam disponíveis. Cada caso usa um
contexto descartável e isolado, sem perfil persistente implícito.

O botão **Validar roteiro** executa um snapshot temporário do rascunho em uma
janela visível do SpyBrowser, Chromium ou CloakBrowser. Antes de iniciar, a
pessoa escolhe a última ação-folha segura que pode ser executada. O painel
destaca o bloco ativo, mostra cards de progresso, permite interromper e exibe
screenshots sanitizadas. Essa homologação não publica o rascunho, não usa o
worker e desabilita write-back de aprendizado.

## Gravar um roteiro no Chrome

O Recorder V2 é uma extensão Manifest V3 que captura interações consentidas,
revisa localmente e exporta um único `.rpablockly.zip`. O pacote interno já usa
os contratos oficiais da V2 e pode ser importado pelo wizard do editor sem edição
manual de JSON.

A RC 9 solicita no primeiro **Iniciar** acesso opcional e persistente a todas as
páginas HTTP(S). Depois do consentimento nativo do Chrome, timeline e evidências
continuam entre origens sem novo clique. A extensão pausa a sessão se esse acesso
for revogado, e toda ação observada sem bloco executável vira pendência bloqueante
com a necessidade de catálogo descrita para decisão.

```powershell
npm ci --ignore-scripts --prefix src/RpaFlow.Recorder.Extension
npm run check --prefix src/RpaFlow.Recorder.Extension
npm run release --prefix src/RpaFlow.Recorder.Extension
```

O build unpacked fica em `src/RpaFlow.Recorder.Extension/build`; o ZIP
reproduzível fica em `artifacts/` e seu checksum versionado fica na pasta
`release/` da extensão. Consulte o
[manual do cliente](docs/recorder/manual-cliente.md) e o
[manual do desenvolvedor](docs/recorder/manual-desenvolvedor.md).

## Executar localmente

Copie a configuração versionável e mantenha segredos somente na cópia local:

```powershell
Copy-Item examples/RpaExemplo/appsettings.example.json `
  examples/RpaExemplo/appsettings.local.json

dotnet run --project examples/RpaExemplo/RpaExemplo.csproj -- --validate-only
dotnet run --project examples/RpaExemplo/RpaExemplo.csproj
```

Opções do host local:

- `--config <arquivo>`: configuração JSON;
- `--package-store <pasta>`: raiz do store de arquivo;
- `--rpa-id <id>`: pacote dentro do store;
- `--revision <sha256>`: fixa revisão; sem ela, usa a atual;
- `--validate-only`: valida pacote e inputs sem abrir navegador.

Para homologar sem terminal, abra o editor, ajuste a **Configuração local**,
clique em **Validar roteiro**, escolha o navegador e confirme a última etapa
segura. A execução usa `Input`, `Attachments` e `Blockly.Variables` da
configuração local; segredos continuam fora do pacote.

## Modos de localização

- `strict`: usa somente o primeiro candidato;
- `fallback`: tenta candidatos exatos na ordem, dentro do orçamento total;
- `adaptive`: depois dos candidatos exatos, permite heurística determinística com
  confiança mínima e diferença mínima para o segundo colocado.

Aprendizado é isolado por `executionId`. Os modos de write-back são `disabled`,
`memory`, `source` e `overlay`. `source` e `overlay` exigem writer explícito e
publicam por compare-and-swap.

## Captchas e intervenção humana

O runtime V2 oferece seis ações: `solveImageCaptcha` usa OCR ONNX embutido,
`solveSliderCaptcha` faz template matching e arraste humanizado,
`solveRecaptchaV2` usa o desafio de áudio, `solveHCaptcha` resolve a grade
binária do hCaptcha com classificadores ResNet ONNX do serviço opcional,
`solveCaptcha` detecta o tipo quando isso é seguro e `waitHumanInput` suspende
até confirmação por arquivo. Cloudflare Turnstile é apenas observado na sessão
original e segue para intervenção humana quando o lifecycle não conclui; o
runtime não produz nem importa tokens.

`captcha-models/` é um cache local ignorado pelo Git: os arquivos ONNX são
binários derivados, enquanto os manifestos versionados guardam origem, versão,
tamanho, SHA-256 e contrato dos tensores. Após um clone, provisione o cache uma
vez:

```powershell
.\tools\Get-CaptchaModels.ps1
.\tools\Get-CaptchaModels.ps1 -Suite hcaptcha
```

O build de `services/captcha-solver/Dockerfile` executa o mesmo provisionamento
dentro da imagem, portanto não depende do cache da máquina. Um clone somente do
Git não é uma distribuição offline: ele ainda precisa acessar as URLs fixadas
nos manifestos ou baixar uma imagem `captcha-solver` previamente publicada em
um registry controlado. Para ambientes sem Internet, publique essa imagem por
digest ou espelhe os artefatos e versione um manifesto com as URLs do espelho;
não versione modelos soltos sem integridade verificável.

Configure limites e, opcionalmente, o serviço HTTP de áudio/OCR de fallback em
`Runtime.Captcha`. Segredos pertencem somente ao `appsettings.local.json`:

```json
"Captcha": {
  "ServiceUrl": "http://127.0.0.1:8855",
  "ServiceApiKey": "troque-esta-chave",
  "OcrModelPath": "../../captcha-models/common.onnx",
  "ServiceTimeoutSeconds": 60,
  "RecaptchaMaxAttempts": 3,
  "HCaptchaMaxAttempts": 3,
  "HumanHandoffTimeoutSeconds": 900,
  "HumanHandoffPollSeconds": 2,
  "DeadlineSeconds": 90,
  "LocalOnly": true,
  "AllowVlmFallback": false,
  "SamePageWaitSeconds": 30,
  "CloudflareSidecarEnabled": false,
  "CloudflareSidecarProvider": "byparr",
  "CloudflareSidecarUrl": "http://127.0.0.1:8191",
  "CloudflareSidecarApiKey": null,
  "CloudflareSidecarTimeoutSeconds": 60,
  "CloudflareSidecarMaximumResponseBytes": 1048576,
  "CloudflareSidecarAllowedHosts": ["sistema.exemplo"]
}
```

`DeadlineSeconds` limita somente a resolução técnica. Ao entrar em handoff,
`HumanHandoffTimeoutSeconds` passa a controlar a espera do operador. Nas ações,
`captcha.maxAttempts` limita tentativas do mesmo desafio dentro desse orçamento;
cada inferência local, pelo serviço ou pelo VLM consome uma unidade compartilhada;
para reCAPTCHA, ele sobrescreve `RecaptchaMaxAttempts`; para hCaptcha, ele
sobrescreve `HCaptchaMaxAttempts`. `ServiceRetryAttempts`
cobre transporte apenas no contrato legado. Requisições V2 não são reenviadas:
sem deduplicação no servidor, uma resposta perdida poderia consumir o orçamento duas vezes.

O serviço Python é opcional para captcha de imagem e obrigatório para o desafio
de áudio do reCAPTCHA v2 e para a grade binária do hCaptcha:

```powershell
docker build -f services/captcha-solver/Dockerfile -t captcha-solver .
docker run --rm -p 127.0.0.1:8855:8855 `
  -e CAPTCHA_API_KEY=troque-esta-chave captcha-solver
```

Contrato e execução sem Docker estão em
[`services/captcha-solver`](services/captcha-solver/README.md). A intervenção
humana grava o pedido e aguarda a resposta em
`<OutputDirectory>/human-handoff/`. Turnstile e Friendly Captcha primeiro
observam o lifecycle na mesma página; somente Turnstile aceita um clique único
quando `captcha.allowInteractiveClick` estiver explicitamente habilitado na ação.
O fallback visual via LiteLLM também é opt-in por `AllowVlmFallback` e continua
sujeito a `LocalOnly`.

Cloudflare Managed Challenge possui fallback experimental para Byparr 3.0.4 ou
FlareSolverr 3.5.0. Ele exige configuração habilitada, domínio na allowlist e
`captcha.kind=cloudflareChallenge` com `captcha.allowCloudflareSidecar=true` na
ação. Somente `cf_clearance` é importado e a página atual é recarregada para
revalidar no `BrowserContext` original. O retorno do sidecar nunca é suficiente
para marcar `Solved`. Implantação, limites e riscos estão em
[`services/cloudflare-sidecar`](services/cloudflare-sidecar/README.md).

As imagens sidecar possuem defaults por digest para builds reproduzíveis, mas
podem ser atualizadas sem editar o Compose por `BYPARR_IMAGE` e
`FLARESOLVERR_IMAGE`. Use sempre referência com digest e rode os checks de
contrato antes de promover uma versão; atualizar a imagem não garante que a API
do provider permaneceu compatível.

O corpus sintético versionado contém 200 imagens, 50 áudios e 30 cenários
geométricos, com relatórios JSON/CSV. Consulte
[`services/captcha-solver/benchmarks`](services/captcha-solver/benchmarks/README.md).

## Worker e banco

O worker SQL faz claim individual, lease, heartbeat e retry. Cada execução carrega
um snapshot independente e persiste origem, revisão e hash usados.

```powershell
Copy-Item src/Rpa.Worker/appsettings.example.json `
  src/Rpa.Worker/appsettings.local.json

dotnet run --project src/Rpa.Worker/Rpa.Worker.csproj -- --validate-only
```

Migrations em ordem:

1. `database/sqlserver/001_create_worker_schema.sql` — fila e histórico;
2. `database/sqlserver/003_worker_resilience.sql` — liderança, heartbeat operacional e recuperação de leases;
3. `003_create_rpa_package_store.sql` — revisões e documentos do pacote;
4. `004_add_execution_package_revision.sql` — revisão/hash na execução;
5. `005_add_locator_diagnostics.sql` — diagnóstico do resolver;
6. `006_add_work_item_lease_fencing.sql` — token único por claim contra escrita tardia.

Pare todos os Workers antigos antes de `006` e reinicie somente a versão com
`LeaseToken`; schema e binário devem avançar juntos nessa migration.

Antes de habilitar claims, confira `RpaWorker.Tables`, configure cada definição,
informe o limite seguro e os IDs irreversíveis, mantenha
`ExecutionMode=SafeValidation` e defina `Enabled=true` por último.

`002_enqueue_example.sql` é apenas uma carga inofensiva de exemplo. Providers de
pacote suportados pelo worker: `File` e `SqlServer`. A conexão e credenciais de
e-mail/Graph devem vir de configuração local, variável de ambiente ou cofre.

## Migrar um fluxo schema 1

O runtime não converte V1 durante a execução. Use o migrador offline:

```powershell
dotnet run --project tools/RpaFlow.Migrator -- `
  caminho\flow.production.json `
  --output tmp\migrado `
  --publish-store packages `
  --rpa-id meu-rpa
```

Use `--dry-run` para apenas validar/relatar, `--batch` para busca recursiva e
`--force` somente quando desejar que a saída existente seja movida para backup.
O migrador nunca sobrescreve a origem e começa com policy `strict`.

## Estrutura do repositório

| Caminho | Responsabilidade |
| --- | --- |
| `schemas/` | JSON Schemas Draft 2020-12 e tipos TypeScript gerados. |
| `src/RpaFlow.Contracts` | DTOs e validadores operacionais V2. |
| `src/RpaFlow.Packages` | snapshots, hash, stores file/memory/inline e registry. |
| `src/RpaFlow.Packages.SqlServer` | provider SQL transacional com CAS. |
| `src/RpaFlow.Runtime` | dados por execução, observer, falhas e orçamento. |
| `src/RpaFlow.Playwright` | resolver, heurística, handlers e artefatos. |
| `src/RpaFlow.Editor` | editor Blockly local e APIs de pacote. |
| `src/RpaFlow.Recorder.Extension` | extensão Chrome MV3, captura, revisão e bundle V2. |
| `src/Rpa.Worker` | consumo SQL, execução, persistência e OTP por Graph. |
| `tools/RpaFlow.Migrator` | conversão offline schema 1 → pacote V2. |
| `tools/RpaFlow.Legacy.Contracts` | contrato histórico isolado. |
| `tools/RpaFlow.RecorderFixture` | site local loopback para aceite strict/fallback do Recorder. |
| `services/captcha-solver` | OCR de fallback e transcrição de áudio self-hosted em CPU. |
| `examples/` e `templates/` | exemplo e scaffold operacionais V2. |
| `tests/` | checks executáveis de contrato, stores, editor, worker e navegador. |

## Testes e release

O gate local completo é:

```powershell
dotnet restore RpaBlockly.slnx
dotnet restore templates/rpa-web/RpaTemplate.csproj
.\tools\Run-Checks.ps1
.\tools\Test-Dependencies.ps1
.\tools\Generate-Sbom.ps1
```

O check SQL aceita `RPABLOCKLY_SQLSERVER_TEST_CONNECTION`. Na CI, o job SQL usa
um SQL Server descartável; localmente ele só usa Docker quando
`RPABLOCKLY_RUN_SQL_DOCKER=true` for definido explicitamente.

O SBOM SPDX 2.3 é gravado em `artifacts/sbom.spdx.json` e inclui NuGet e os dois
inventários npm. Metadados do release candidate ficam em
`release/2.0.0-rc.1.json`.

## Artefatos e dados

- `input.*`: dados imutáveis do caso;
- `config.*`: parâmetros administrativos não secretos;
- `attachments.*`: anexos autorizados;
- `runtime.*`: valores produzidos pelo fluxo;
- `system.*`: IDs de execução/item/lote;
- `loop.*`: item e índice ativos.

Screenshots, downloads e diagnósticos usam `Runtime.OutputDirectory`. Tamanho,
quantidade e retenção são limitados por `MaximumArtifactBytes`,
`MaximumArtifactFilesPerExecution` e `ArtifactRetentionDays`. HTML de falha é
sanitizado e limitado.

## Segurança e manutenção

- não versione `appsettings.local.json`, storage state, certificados, tokens ou
  strings de conexão reais;
- não grave segredo em flow, locators, policy, inputs persistidos ou logs;
- valide package e inputs antes do navegador;
- mantenha schemas, DTOs, tipos gerados, Blockly, handlers e checks na mesma
  mudança;
- publique nova revisão em vez de editar diretórios de revisão;
- use o histórico e CAS para rollback; nunca combine documentos de revisões
  diferentes.

Documentação detalhada: [docs/README.md](docs/README.md),
[ADRs](docs/adr/README.md) e [guia do pacote V2](docs/v2/pacote-operacional.md).
