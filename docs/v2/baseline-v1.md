# Baseline V1 para a migração

Baseline: commit `03b74fe2197ad7651f4ba05ec5819efa9787f194`.

## Inventário confirmado

- 32 tipos de ação no `FlowActionCatalog`;
- 35 blocos distintos no editor;
- um fluxo schema 1 com ações e subfluxos;
- seletores embutidos em ação ou condição;
- worker com claim individual e paralelismo independente;
- editor com persistência UTF-8 estrita, temporário e backup.

O catálogo operacional atual acrescenta o marcador idempotente
`completeAuthenticationAttempt` para a cerca de repetição do login. Por isso, a
verificação de compatibilidade exercita 33 tipos de ação e o editor atual possui
36 blocos, sem alterar a semântica dos 32 tipos do commit de baseline.

## Fonte versionada do inventário

O inventário executável fica em
`tests/RpaFlow.MigratorChecks/Fixtures/baseline-v1/inventory.json`. Ele é gerado
pelo próprio `FlowActionCatalog`, associa cada tipo a uma família e a uma fixture
V1 e registra os checks observáveis do baseline. Assim, a documentação não mantém
uma segunda lista manual sujeita a divergência.

As fixtures `navigation.json`, `form.json`, `data-artifact.json`, `control.json` e
`aggregate-33.json` são sanitizadas, validadas como V1 e migradas para um pacote
V2 válido em todo check. Para atualizar deliberadamente esse baseline:

```powershell
dotnet run --project tests/RpaFlow.MigratorChecks -- --write-baseline
```

## Campos de localização a migrar

| V1 | V2 |
|---|---|
| `selector` | `{actionId}.target` |
| `scope`, `scopeHasText`, `scopeHasTextSource` | receita de `{actionId}.target`, no nível de scope |
| `hasText`, `hasTextSource` | filtro do target em `{actionId}.target` |
| `frameSelectors` | frames externos → internos da receita de `{actionId}.target` |
| condição de elemento | `{actionId}.condition` |
| `triggerSelector` | `{actionId}.trigger` |
| `optionSelector` | `{actionId}.options` |
| `readySelector` | `{actionId}.ready` |
| `successSelector` | `{actionId}.success` |
| `protocolSelector` | `{actionId}.protocol` |
| `matchMode: single` | `cardinality: single` |
| `matchMode: first` | `cardinality: first` |
| coleção | `cardinality: many` |
| `target` como destino `runtime.*` | `output` |

## Regra de cobertura

`RpaFlow.MigratorChecks` compara o inventário e a união das fixtures ao catálogo
compilado. Um tipo novo falha o gate até receber família, fixture, estratégia de
migração e documentação. `RpaFlow.PlaywrightChecks` mantém o diferencial de
execução estrita V1/V2, e `RpaFlow.EditorRoundTrip` mede o ciclo abrir → salvar →
reabrir.
