# Worker genérico

Worker SQL Server para executar qualquer definição desta base. Ele gerencia polling, claim atômico, lease, heartbeat, timeout, retry, histórico, eventos, outputs, artefatos e storage state por `SessionKey`. Cada caso carrega um snapshot imutável do pacote V2 antes da primeira ação e registra revisão e hash usados.

O worker também inclui um `IOneTimeCodeProvider` para Outlook/Microsoft 365. A implementação consulta e-mails pelo Microsoft Graph; ela não depende do Outlook aberto e não altera as mensagens.

O fluxo Blockly nunca consulta a fila. Ele recebe um `FlowExecutionRequest` já isolado e devolve `runtime`.

## Configurar

```powershell
Copy-Item src/Rpa.Worker/appsettings.example.json `
  src/Rpa.Worker/appsettings.local.json
dotnet run --project src/Rpa.Worker/Rpa.Worker.csproj -- --validate-only
```

O arquivo local pode conter a string de conexão e fica ignorado. O exemplo permanece vazio, desabilitado e em `SafeValidation`.

## OTP por e-mail

O bloco `waitForOneTimeCode` usa `providerAlias` para selecionar uma entrada de `RpaWorker.EmailReader.Providers`. O fluxo não recebe tenant, client ID, segredo, caixa postal ou expressão regular.

Para habilitar:

1. registre um aplicativo no Microsoft Entra ID;
2. conceda a permissão de aplicativo `Mail.Read` com consentimento administrativo;
3. restrinja o aplicativo às caixas necessárias;
4. copie o exemplo para `appsettings.local.json`;
5. preencha `TenantId`, `ClientId`, `ClientSecret` e o provider;
6. execute `--validate-only` antes de habilitar claim.

O segredo também pode ser fornecido sem gravá-lo em arquivo:

```powershell
$env:RpaWorker__EmailReader__ClientSecret = "<forneça-em-tempo-de-execução>"
```

O provider:

- considera somente mensagens posteriores ao marco do bloco e dentro de `MaximumEmailAgeMinutes`;
- filtra por trecho do assunto e, opcionalmente, por endereço exato do remetente;
- escolhe a mensagem válida mais recente;
- extrai o primeiro grupo de captura de `CodePattern`;
- repete apenas a leitura até `timeoutMs`, respeitando `pollIntervalMs`;
- serializa consultas do mesmo alias;
- não marca, move nem exclui e-mails.

Uma definição com OTP e `ClaimEnabled=true` exige `MaxParallelism=1`. Os destinos temporários de OTP são removidos do `OutputJson`; mapeá-los como output ou artefato é recusado na inicialização.

Consulte o [guia de integração do worker](../../docs/referencia-markdown/integracao-worker-banco.md) e o [tutorial de solução de problemas](../../docs/referencia-markdown/tutorial-solucao-problemas-rpa-blockly.md).

## Três intertravamentos

Para um item ser reservado, todos precisam estar verdadeiros:

1. `RpaWorker.Enabled`;
2. `Definitions.<código>.Enabled`;
3. `Definitions.<código>.ClaimEnabled`.

Em `SafeValidation`, há duas formas genéricas de encerrar uma homologação:

- com `SafeValidationBoundaryActionId`, o worker executa a ação indicada, encerra imediatamente depois dela e grava o item como `Validated`, preservando os resultados e artefatos produzidos até o limite;
- sem esse limite explícito, permanece o comportamento compatível: o primeiro ID de `IrreversibleActionIds` é bloqueado antes da execução e o item termina como `Validated`.

O ID do limite precisa existir no fluxo, apontar para uma ação-folha e não pode também ser irreversível. Se o fluxo terminar sem alcançá-lo, ou alcançar antes uma ação irreversível, a execução falha porque o roteiro não corresponde à configuração homologada. Em `Production`, o limite de validação não interrompe o fluxo e o guard permite os IDs irreversíveis; essa mudança deve seguir autorização e procedimento operacional.

## Inputs e resultados

- `WorkItem.InputJson` → `input.*`;
- `WorkItem.ConfigurationJson` → complementa `config.*`;
- `WorkItem.AttachmentsJson` → complementa `attachments.*`;
- `FlowExecutionResult.Output` → `WorkItem.OutputJson`;
- `Definitions.Outputs` → linhas em `ExecutionOutput`;
- `Definitions.Artifacts` → arquivos verificados e linhas em `Artifact`.

Consulte o [guia de integração](../../docs/referencia-markdown/integracao-worker-banco.md) e o [README do banco](../../database/sqlserver/README.md) para cada propriedade e tabela.
