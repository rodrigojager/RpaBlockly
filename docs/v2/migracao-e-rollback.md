# Migração e rollback

## Migração offline

O runtime V2 não lê schema 1. Execute primeiro um dry-run:

```powershell
dotnet run --project tools/RpaFlow.Migrator -- `
  caminho\flow.production.json --dry-run
```

Para gerar documentos e publicar no file store:

```powershell
dotnet run --project tools/RpaFlow.Migrator -- `
  caminho\flow.production.json `
  --output tmp\migrado `
  --publish-store package-store `
  --rpa-id meu-rpa
```

O migrador valida V1, preserva IDs/ordem, cria um locator por papel, usa
`rawPlaywright`, não deduplica seletores, cria policy `strict` e grava
`migration-report.json`. A origem nunca é sobrescrita.

Em lote, use `--batch`. `--force` move a saída existente para uma pasta de backup
antes de criar a nova; não o use sem revisar o destino.

## Verificação do cutover

1. revise warnings e possíveis duplicidades do relatório;
2. abra o pacote no editor V2;
3. salve e reabra, confirmando a revisão;
4. execute `--validate-only`;
5. execute em `strict` contra ambiente seguro;
6. compare outputs, ações e artefatos ao baseline histórico;
7. só então aponte o worker para o novo package store/revisão.

## Rollback

Para conteúdo, publique por CAS o conteúdo de uma revisão anterior ou fixe
explicitamente seu hash na configuração. Para rollback de aplicação, restaure o
release anterior e a origem V1 preservada. Não existe conversão reversa automática
e nunca se deve combinar documentos de revisões diferentes.
