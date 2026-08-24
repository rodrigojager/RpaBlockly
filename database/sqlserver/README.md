# Banco genérico do worker

O script `001_create_worker_schema.sql` cria a fila e o histórico de execução
sem conhecer o domínio do RPA. Ative o modo SQLCMD no SSMS ou execute com
`sqlcmd`; os nomes do schema e das cinco tabelas ficam no início do arquivo e
precisam coincidir com `RpaWorker.Tables`. O script
`003_worker_resilience.sql` acrescenta o estado operacional e as colunas usadas
para liderança, recuperação e retry seguro.

Depois do baseline, aplique as migrations V2 na ordem:

1. `003_create_rpa_package_store.sql`: revisão, documentos e ponteiro atual;
2. `004_add_execution_package_revision.sql`: origem, revisão e hash executados;
3. `005_add_locator_diagnostics.sql`: identidade do pacote nos eventos.

Todos os scripts são idempotentes para os objetos que criam ou acrescentam.

## Responsabilidade de cada tabela

| Tabela padrão | Finalidade |
| --- | --- |
| `rpa.WorkItem` | Fila, prioridade, claim, lease, retry, input, configuração por caso, anexos e output completo. |
| `rpa.Execution` | Uma tentativa de execução, com worker, tempos, status e falha. |
| `rpa.ExecutionOutput` | Outputs nomeados extraídos de `runtime.*` pela configuração da definição. |
| `rpa.Artifact` | Arquivos efetivamente existentes, com caminho, tamanho e SHA-256. |
| `rpa.ExecutionEvent` | Telemetria de início, ações, conclusão, cancelamento e falha. |
| `rpa.WorkerState` | Heartbeat operacional, liderança, polling, capacidade e encerramento de cada instância. |
| `rpa.RpaPackageRevision` | Metadados e hash de cada revisão imutável. |
| `rpa.RpaPackageDocument` | Os três documentos pertencentes à mesma revisão. |
| `rpa.RpaPackageCurrent` | Ponteiro atual por RPA, usado no compare-and-swap. |

Os nomes podem mudar administrativamente, mas nunca vêm do fluxo Blockly. O worker aceita somente identificadores SQL simples, os coloca entre colchetes e usa parâmetros para todos os valores.

## JSON do item

- `InputJson` alimenta `input.*` e representa os dados imutáveis do caso.
- `ConfigurationJson` complementa `config.*` e deve conter apenas parâmetros não secretos.
- `AttachmentsJson` alimenta `attachments.*` com caminhos previamente autorizados.
- `OutputJson` recebe a cópia completa de `runtime` ao final.

Credenciais não devem entrar nesses JSONs. Use identidade do serviço, cofre de segredos ou um provedor protegido injetado no host.

## Primeira execução

1. Ajuste os `:setvar` e execute `001_create_worker_schema.sql`, `003_worker_resilience.sql`, `003_create_rpa_package_store.sql`, `004_add_execution_package_revision.sql` e `005_add_locator_diagnostics.sql` nessa ordem.
2. Copie `src/Rpa.Worker/appsettings.example.json` para `appsettings.local.json`.
3. Informe `ConnectionStrings.RpaDatabase` apenas no arquivo local.
4. Configure `Definitions.<id>.Package` com provider, origem, RPA e localização.
5. Mantenha `ExecutionMode` como `SafeValidation`.
6. Cadastre os IDs irreversíveis de cada fluxo.
7. Habilite `ClaimEnabled` somente na definição que será testada.
8. Defina `RpaWorker.Enabled=true` por último.

O script `002_enqueue_example.sql` cria um item inofensivo para o fluxo mínimo de exemplo. Ele só será reservado quando a definição `exemplo` e o worker forem explicitamente habilitados.
