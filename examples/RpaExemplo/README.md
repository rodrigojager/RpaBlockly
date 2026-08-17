# RPA de exemplo

Projeto mínimo usado para provar que a base compila, o editor abre e o pacote V2
é válido. Ele não acessa sistema real: a única ação grava
`runtime.estado = "pronto"`.

```powershell
dotnet run --project examples/RpaExemplo/RpaExemplo.csproj -- --validate-only
.\abrir-editor.cmd examples\RpaExemplo
```

Crie RPAs reais com `tools/Novo-Rpa.ps1`; não transforme este exemplo em automação de produção.

O pacote fica em `package-store/rpa-exemplo`: o ponteiro `current.json` referencia
uma revisão imutável com fluxo schema 2, catálogo de locators e policy `strict`.
