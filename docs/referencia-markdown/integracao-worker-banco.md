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
| `rpa.WorkerState` | heartbeat operacional, liderança, polling, capacidade e encerramento. |
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

Execute `001_create_worker_schema.sql`, `003_worker_resilience.sql`,
`003_create_rpa_package_store.sql`, `004_add_execution_package_revision.sql` e
`005_add_locator_diagnostics.sql` em ordem, com SQLCMD e os mesmos nomes de
schema/tabelas configurados no worker. `002` apenas insere exemplo.

## Retry e isolamento

Falhas transitórias podem ser marcadas retryable. O guard existente registra
efeito irreversível concluído e impede retry automático depois desse ponto. Uma
publicação concorrente não muda a revisão de uma execução já iniciada.

Uma trava global por sessão SQL impede duas instâncias de consumir a mesma
implantação. Se a conexão ou a trava cair, os claims são suspensos, as execuções
da sessão são canceladas de forma controlada e o host tenta readquirir a liderança
com backoff. Falhas de banco no polling degradam a prontidão, mas não encerram o
processo. Leases vencidos são recuperados automaticamente no ciclo seguinte.

`/health/live` confirma que o processo HTTP responde. `/health/ready` também
exige validação, liderança e polling recentes; `acceptingClaims` informa
separadamente se existe vaga imediata. A tabela configurada em `Tables.Workers`
recebe o heartbeat operacional persistente.

Quando uma ação declarada em `AuthenticationAttemptActionIds` começa, uma falha
anterior ao marcador `completeAuthenticationAttempt` não repete o login
automaticamente. Depois do marcador, uma instabilidade transitória pode consumir
a próxima tentativa. MFA permanece cercado separadamente por
`MfaAttemptActionIds`.

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
