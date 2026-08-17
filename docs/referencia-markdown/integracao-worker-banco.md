# Integração do worker V2 com SQL Server

## Fila e execução

`Rpa.Worker` reserva um `WorkItem` por claim atômico, mantém lease/heartbeat e
processa casos em paralelo até `MaxParallelism`. Cada caso cria seu próprio
`FlowExecutionRequest`, snapshot e contexto; não há sincronização por etapa.

Tabelas padrão:

| Tabela | Leitura/escrita |
| --- | --- |
| `rpa.WorkItem` | fila, prioridade, claim, lease, input/config/anexos, retry e output. |
| `rpa.Execution` | tentativa, status, tempos, falha, origem/revisão/hash. |
| `rpa.ExecutionOutput` | outputs nomeados extraídos de `runtime.*`. |
| `rpa.Artifact` | caminho, tamanho e SHA-256 de arquivo existente. |
| `rpa.ExecutionEvent` | eventos de runtime e resolução de locator. |
| `rpa.RpaPackageRevision` | metadados imutáveis de cada revisão. |
| `rpa.RpaPackageDocument` | os três JSONs de uma revisão. |
| `rpa.RpaPackageCurrent` | ponteiro atual por RPA. |

## Configuração de pacote

Cada definição usa `Package`:

```json
{
  "RpaId": "meu-rpa",
  "OriginName": "source",
  "Provider": "File",
  "Location": "packages",
  "Revision": null
}
```

Providers: `File` e `SqlServer`. `Revision` nula resolve a atual; preenchida fixa
um SHA-256. `Overlay` opcional possui origem distinta e é usado somente quando a
policy solicita write-back overlay.

## Migrations

Execute `001`, `003`, `004` e `005` em ordem, com SQLCMD e os mesmos nomes de
schema/tabelas configurados no worker. `002` apenas insere exemplo.

## Retry e isolamento

Falhas transitórias podem ser marcadas retryable. O guard existente registra
efeito irreversível concluído e impede retry automático depois desse ponto. Uma
publicação concorrente não muda a revisão de uma execução já iniciada.

## Segredos

`InputJson`, `ConfigurationJson`, `AttachmentsJson` e documentos do pacote não são
cofre. String de conexão, segredo Graph e credenciais devem vir de
`appsettings.local.json`, variáveis de ambiente, identidade do serviço ou cofre.
Outputs sensíveis, como código de uso único, são removidos da cópia persistida.

## Validação

```powershell
dotnet run --project src/Rpa.Worker -- --validate-only
dotnet run --project tests/Rpa.WorkerChecks
```

O check SQL real usa `RPABLOCKLY_SQLSERVER_TEST_CONNECTION` ou, somente quando
autorizado, `RPABLOCKLY_RUN_SQL_DOCKER=true`.
