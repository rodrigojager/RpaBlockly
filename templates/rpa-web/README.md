# Novo RPA web

Este projeto foi criado a partir da base Blockly. Prefira executar `tools/Novo-Rpa.ps1` na raiz em vez de copiar esta pasta manualmente.

## Arquivos do RPA

- `Program.cs`: host local, UTF-8 estrito, `--config`, `--flow` e `--validate-only`;
- `appsettings.example.json`: configuração versionável sem segredo;
- `appsettings.local.json`: configuração operacional ignorada pelo Git;
- `flow.production.json`: contrato executado em produção;
- `rpa.editor.json`: perfil do Blockly;
- `RpaTemplate.csproj`: referências para contrato, runtime e Playwright compartilhados.

## Comandos

```powershell
dotnet build RpaTemplate.csproj
dotnet run --project RpaTemplate.csproj --no-build -- --validate-only
.\abrir-editor.cmd rpas\RpaTemplate
```

## Regras essenciais

1. Declare dados e anexos obrigatórios em `inputs` para falhar antes do navegador.
2. Coloque dados do caso em `input.*`, parâmetros em `config.*` e anexos em `attachments.*`.
3. Grave resultados somente em `runtime.*`.
4. Mantenha seletores e ordem no fluxo, não em código específico.
5. Audite seletores e readiness no sistema real sem efeito irreversível.
6. Credenciais ficam fora do fluxo e fora do Git.
7. Valide JSON → Blockly → JSON antes de promover o fluxo.

Consulte `docs/manual.html` na raiz para o guia completo e todas as propriedades dos blocos.
