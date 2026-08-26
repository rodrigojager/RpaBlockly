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

- .NET SDK `9.0.300` ou patch compatível, conforme `global.json`;
- PowerShell 7 para os scripts de verificação;
- Node.js 24 e npm para conformidade TypeScript dos schemas;
- Chromium do Playwright para os checks de navegador;
- SQL Server apenas para o worker/store SQL; os checks locais normais não iniciam
  Docker.

```powershell
dotnet restore RpaBlockly.slnx
dotnet build RpaBlockly.slnx --configuration Release
pwsh src/RpaFlow.Playwright/bin/Release/net9.0/playwright.ps1 install chromium
```

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

No editor, os 36 blocos cobrem os 33 tipos de ação. O pacote é aberto por revisão;
salvar publica os três documentos atomicamente. Conflito de revisão nunca
sobrescreve alterações silenciosamente.

O botão **Validar roteiro** executa um snapshot temporário do rascunho em uma
janela visível do Chromium ou CloakBrowser. Antes de iniciar, a pessoa escolhe a
última ação-folha segura que pode ser executada. O painel destaca o bloco ativo,
mostra cards de progresso, permite interromper e exibe screenshots sanitizadas.
Essa homologação não publica o rascunho, não usa o worker e desabilita write-back
de aprendizado.

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
5. `005_add_locator_diagnostics.sql` — diagnóstico do resolver.

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
