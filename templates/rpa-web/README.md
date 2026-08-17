# Novo RPA web V2

Este projeto foi criado a partir do template V2. Use `tools/Novo-Rpa.ps1` na raiz
da base em vez de copiar a pasta manualmente.

## Arquivos do RPA

- `Program.cs`: host local do pacote V2;
- `appsettings.example.json`: configuração versionável sem segredo;
- `appsettings.local.json`: configuração operacional ignorada pelo Git;
- `package-store/rpa-template`: revisões atômicas do pacote;
- `rpa.editor.json`: perfil que liga o editor ao RPA e ao package store;
- `RpaTemplate.csproj`: referências ao runtime compartilhado.

Cada revisão contém `flow.production.json`, `locators.production.json` e
`rpa.policy.json`. O fluxo guarda referências `locatorId`; seletores, frames,
scope e fingerprints ficam somente no catálogo.

## Comandos

```powershell
dotnet build RpaTemplate.csproj
dotnet run --project RpaTemplate.csproj --no-build -- --validate-only
.\abrir-editor.cmd rpas\RpaTemplate
```

Também é possível fixar uma revisão com `--revision <sha256>` ou escolher outro
store com `--package-store <pasta>` e `--rpa-id <id>`.

## Regras essenciais

1. Declare inputs antes de abrir o navegador.
2. Grave resultados somente em `runtime.*`.
3. Edite fluxo, locators e policy como uma única revisão.
4. Comece em política `strict`; habilite fallback ou adaptive deliberadamente.
5. Mantenha credenciais fora do pacote, logs e screenshots.
6. Use `--validate-only` e a suíte da base antes de publicar.

Consulte `README.md` e `docs/v2/` na raiz para o guia completo.
