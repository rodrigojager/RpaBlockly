# Integração do worker com o banco

## Estado atual

A base contém um worker genérico em `src/Rpa.Worker`. Ele lê itens de uma fila SQL Server, monta um `FlowExecutionRequest` isolado e executa o mesmo `flow.production.json` usado pelo host local:

```text
fila SQL + definição do RPA
  → claim atômico e lease
  → input, configuração e anexos do caso
  → FlowExecutionRequest
  → runtime compartilhado
  → FlowExecutionResult
  → status, outputs, artefatos e eventos
```

Consulte também:

- [README operacional do worker](../../src/Rpa.Worker/README.md);
- [estrutura SQL genérica](../../database/sqlserver/README.md);
- [arquitetura e execução](arquitetura-e-execucao.md);
- [schema JSON versão 1](flow-schema-v1.md).

## Divisão de responsabilidades

### Sistema de entrada

O sistema que cria o item de trabalho é responsável por validar os dados do caso, autorizar os caminhos dos anexos e escolher o código do RPA. Credenciais e segredos não pertencem a `InputJson`, `ConfigurationJson` ou `AttachmentsJson`.

### Worker

O worker:

- seleciona e reserva o próximo item elegível;
- controla lease, heartbeat, timeout, tentativas e paralelismo;
- carrega e valida a definição e o fluxo;
- cria um request novo para cada item;
- aplica o limite explícito de validação segura, quando configurado, ou bloqueia as ações configuradas como irreversíveis;
- executa o runtime fora da transação de claim;
- persiste resultado, outputs nomeados, artefatos e eventos;
- remove códigos de uso único do output persistido.

### Fluxo Blockly

O fluxo atua somente sobre o caso recebido. Ele lê `input.*`, `config.*`, `attachments.*` e `system.*`, grava apenas em `runtime.*` e não escolhe o próximo item nem executa SQL.

## Contrato de dados

O mapeamento padrão é:

| Banco | Contexto do fluxo |
| --- | --- |
| `WorkItem.InputJson` | `input.*` |
| `WorkItem.ConfigurationJson` | complementa `config.*` |
| `WorkItem.AttachmentsJson` | complementa `attachments.*` |
| ID do item e lote | `system.workItemId` e `system.batchId` |
| `FlowExecutionResult.Output` | `WorkItem.OutputJson` |

A configuração base pode fornecer `Blockly.Variables` e anexos comuns. Os objetos do item são mesclados em cópias isoladas antes da execução. Dados de um caso não são compartilhados com outro.

## Claim, lease e retry

O claim usa uma transação curta com `UPDLOCK`, `READPAST` e `ROWLOCK`. Somente códigos com `Enabled=true` e `ClaimEnabled=true` entram na seleção. Um item `Pending` ou `Retry` passa para `Running`, recebe o identificador do worker, uma expiração de lease e o incremento da tentativa.

Durante a navegação, o heartbeat renova o lease. Se o item deixar de pertencer ao worker, a renovação ou a finalização falha em vez de gravar sobre o estado de outra instância. Uma falha comum volta para `Retry` enquanto ainda houver tentativa; depois passa para `Failed`.

Uma trava global por sessão SQL impede duas instâncias de consumir a mesma implantação. Se a conexão ou a trava cair, os claims são suspensos, as execuções da sessão são canceladas de forma controlada e o host tenta readquirir a liderança com backoff. Falhas de banco no polling degradam a prontidão, mas não encerram o processo. Leases vencidos são recuperados automaticamente no ciclo seguinte.

`/health/live` confirma que o processo HTTP responde. `/health/ready` também exige validação, liderança e polling recentes; `acceptingClaims` informa separadamente se existe vaga imediata. A tabela configurada em `Tables.Workers` recebe o heartbeat operacional persistente.

Quando uma ação declarada em `AuthenticationAttemptActionIds` começa, uma falha anterior ao marcador `completeAuthenticationAttempt` não repete o login automaticamente. Depois do marcador, uma instabilidade transitória pode consumir a próxima tentativa. MFA permanece cercado separadamente por `MfaAttemptActionIds`.

Nenhuma transação de banco permanece aberta durante a automação do sistema externo.

## Validação segura e produção

A configuração possui dois modos e um limite opcional por definição:

- `SafeValidation`: quando `SafeValidationBoundaryActionId` está preenchido, executa essa ação e encerra imediatamente depois dela como `Validated`; sem o limite explícito, interrompe antes do primeiro ID listado em `IrreversibleActionIds`;
- `Production`: permite que o handler alcance esses IDs.

O limite explícito precisa referenciar uma ação-folha existente e não pode ser também irreversível. A ação-folha pode estar dentro de condição, repetição ou subfluxo; o que não pode ser usado como limite é o próprio bloco composto. Se o limite estiver configurado, terminar sem alcançá-lo ou alcançar antes um ID irreversível é falha, não validação bem-sucedida. Depois que a ação-limite termina, as ações restantes não começam, e os outputs e artefatos já produzidos continuam disponíveis para persistência.

A mudança de modo não substitui a política específica exigida por `safeFinalConfirmation` e não transforma um clique comum em confirmação protegida. O bloco pode descrever como comprovar mensagem e protocolo, mas somente o host decide se o efeito irreversível está autorizado.

O worker genérico também não infere que uma navegação concluída representa sucesso de negócio. Quando um domínio exige comprovação adicional, o projeto deve usar uma política específica, produzir um resultado explícito em `runtime.*` e validar esse resultado antes de considerar a operação concluída.

## Outputs, artefatos e sessões

Cada definição pode mapear valores de `runtime.*` para `ExecutionOutput`. Um output marcado como obrigatório precisa existir. Valores sensíveis devem ser declarados como tal e nunca usados em logs.

Artefatos mapeados são materializados somente depois que o arquivo existe. O worker registra caminho, tamanho e SHA-256. Caminhos relativos permanecem confinados ao workspace ou ao diretório de artefatos configurado.

Quando `UseSessionState` está habilitado e o item possui `SessionKey`, o caminho do estado do navegador é derivado de um hash de `RpaCode` e `SessionKey`. O arquivo é sensível e deve permanecer fora do Git.

## Código de uso único por e-mail

`waitForOneTimeCode` usa um `providerAlias` configurado em `RpaWorker.EmailReader.Providers`. Tenant, client ID, segredo, caixa postal e expressão regular ficam na configuração protegida do worker, nunca no fluxo.

Uma definição que faz claim e usa esse provider exige `MaxParallelism=1`. O worker recusa mappings que possam persistir o caminho de runtime do código temporário e remove esse valor do output completo.

## Implantação mínima

1. Aplique `database/sqlserver/001_create_worker_schema.sql` e `003_worker_resilience.sql`.
2. Copie `src/Rpa.Worker/appsettings.example.json` para `appsettings.local.json`.
3. Configure a string de conexão somente no arquivo local, em variável de ambiente ou em cofre.
4. Cadastre cada definição, fluxo, configuração, limite seguro opcional e IDs irreversíveis.
5. Execute `--validate-only`.
6. Mantenha `ExecutionMode=SafeValidation` durante a homologação.
7. Habilite um `ClaimEnabled` por vez e defina `RpaWorker.Enabled=true` por último.

Os nomes de schema e tabelas são aceitos somente como identificadores SQL simples. Valores são parametrizados, e o fluxo não pode fornecer SQL, tabela ou coluna livre.
