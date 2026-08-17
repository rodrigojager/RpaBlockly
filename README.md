# Base RPA Blockly

Base independente para criar RPAs web com editor visual Blockly, contrato JSON schema 1, runtime .NET 9, Playwright, worker SQL Server, OTP por e-mail via Microsoft Graph, inputs tipados, outputs nomeados, artefatos verificados e sessões separadas por login.

O objetivo é permitir que um novo RPA seja criado por configuração e composição de blocos. A ordem, seletores, valores, condições, loops e subfluxos ficam no JSON; o C# compartilhado cresce somente quando surge uma capacidade técnica realmente nova.

## Comece aqui

Abra [docs/manual.html](docs/manual.html) no navegador ou execute:

```powershell
.\abrir-manual.cmd
```

O manual funciona localmente, sem CDN, possui busca, filtro, tema e uma seção independente para cada um dos 35 blocos, com todas as propriedades e opções.

## Criar um RPA

```powershell
.\tools\Novo-Rpa.ps1 `
  -Name RpaContasPagar `
  -DisplayName "Contas a pagar"
```

O gerador:

- valida o nome;
- copia `templates/rpa-web`;
- renomeia o projeto e ajusta referências relativas;
- cria `appsettings.local.json`, que está ignorado pelo Git;
- atualiza nome do fluxo e perfil do editor;
- adiciona o projeto a `RpaBlockly.slnx`.

Depois:

```powershell
dotnet build RpaBlockly.slnx
dotnet run --project rpas/RpaContasPagar/RpaContasPagar.csproj --no-build -- --validate-only
.\abrir-editor.cmd rpas\RpaContasPagar
```

## Componentes

| Pasta | Responsabilidade |
| --- | --- |
| `src/RpaFlow.Contracts` | Schema, catálogo e validações. |
| `src/RpaFlow.Runtime` | Contexto isolado, dados, limites, observer e contratos. |
| `src/RpaFlow.Playwright` | Automação web, handlers, readiness e arquivos. |
| `src/RpaFlow.Editor` | Blockly local e microservidor de edição. |
| `src/Rpa.Worker` | Claim, lease, heartbeat, retry, OTP por Microsoft Graph, outputs, artefatos e sessões. |
| `templates/rpa-web` | Scaffold usado pelo gerador. |
| `database/sqlserver` | Tabelas genéricas configuráveis do worker. |
| `tests` | Verificação da base, navegador local e round-trip. |
| `docs` | Manual HTML e referências técnicas. |

## Worker

O exemplo nasce desabilitado e em `SafeValidation`:

```powershell
Copy-Item src/Rpa.Worker/appsettings.example.json `
  src/Rpa.Worker/appsettings.local.json

dotnet run --project src/Rpa.Worker/Rpa.Worker.csproj -- --validate-only
```

Configure a string de conexão somente no arquivo local. Antes de ligar o worker:

1. aplique `database/sqlserver/001_create_worker_schema.sql`;
2. confira os nomes em `RpaWorker.Tables`;
3. configure cada entrada de `Definitions`;
4. se a homologação deve executar uma última ação segura, informe seu ID em `SafeValidationBoundaryActionId`;
5. liste todas as ações irreversíveis em `IrreversibleActionIds`;
6. habilite `ClaimEnabled` somente no RPA em teste;
7. mantenha `ExecutionMode=SafeValidation`;
8. defina `Enabled=true` por último.

Se o fluxo usa `waitForOneTimeCode`, configure `RpaWorker.EmailReader`, mantenha as credenciais apenas no arquivo local, em variáveis de ambiente ou em um cofre, e use `MaxParallelism=1` enquanto uma definição com OTP estiver fazendo claim. O manual detalha isso em `docs/manual.html#otp-email`.

A referência [Integração do worker com o banco](docs/referencia-markdown/integracao-worker-banco.md) descreve claim, lease, retry, isolamento do request, outputs, artefatos e a separação entre worker e fluxo.

## Dados por execução

- `input.*`: dados imutáveis do caso;
- `config.*`: parâmetros administrativos não secretos;
- `attachments.*`: caminhos de anexos;
- `runtime.*`: valores produzidos pelo fluxo;
- `system.*`: IDs de execução, item e lote;
- `loop.*`: item e índice dos loops ativos.

O worker grava o `runtime` completo em `WorkItem.OutputJson` e pode materializar outputs e artefatos nomeados por mapeamentos configuráveis.

## Validar a base

```powershell
.\tools\Validar-Base.ps1
```

Ou execute individualmente:

```powershell
dotnet build RpaBlockly.slnx
dotnet run --project tests/RpaBase.Checks/RpaBase.Checks.csproj --no-build
dotnet run --project tests/Rpa.WorkerChecks/Rpa.WorkerChecks.csproj --no-build
dotnet run --project tests/RpaFlow.PlaywrightChecks/RpaFlow.PlaywrightChecks.csproj --no-build
dotnet run --project examples/RpaExemplo/RpaExemplo.csproj --no-build -- --validate-only
dotnet run --project src/Rpa.Worker/Rpa.Worker.csproj --no-build -- --validate-only
```

## Segurança

- Não versione `appsettings.local.json`, banco real, senhas, tokens ou storage state.
- Não coloque credenciais em `flow.production.json`, inputs, outputs ou logs.
- Não permita que o Blockly execute SQL livre ou escolha o próximo caso.
- Não use pausas fixas como sincronização.
- Não use seletor ambíguo, `First`, `Nth` ou clique forçado para fazer um teste passar.
- Não ultrapasse uma ação irreversível sem autorização explícita.
- No bloco de confirmação final, publicar feedback descreve a evidência esperada; somente o host e a política específica podem autorizar o efeito.
- Mantenha Blockly, JSON, validadores, handlers, documentação e testes sincronizados.
