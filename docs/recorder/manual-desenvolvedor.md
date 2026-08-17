# Manual do desenvolvedor — Recorder V2

## Fluxo técnico

O content script aceita somente eventos confiáveis do navegador e cria
observações sanitizadas. O service worker atribui sequência e mantém checkpoint
em `chrome.storage.session`. Normalização, autoria de locators e geração produzem
os três documentos oficiais da V2. O bundle ordena entradas, fixa o timestamp do
ZIP e calcula SHA-256 por arquivo.

No editor, o ZIP é tratado como não confiável: a inspeção ocorre sem extração,
antes da desserialização, com limites de tamanho, razão de compressão, caminhos,
tipo de entrada e integridade. `inspect` e `validate` não publicam. `apply` exige
revisão esperada, mappings explícitos e compare-and-swap.

## Preparar o ambiente

```powershell
dotnet restore RpaBlockly.slnx
npm ci --ignore-scripts --prefix src/RpaFlow.Recorder.Extension
npm run check --prefix src/RpaFlow.Recorder.Extension
dotnet build RpaBlockly.slnx --configuration Release --no-restore
pwsh src/RpaFlow.Playwright/bin/Release/net9.0/playwright.ps1 install chromium
.\tools\Run-Checks.ps1
```

O E2E usa o content script compilado em `build/`, grava a fixture de navegador,
gera o bundle pelo código de produção, importa no editor, publica no file store e
executa em `strict` e `fallback`.

## Importar no editor

1. abra o pacote de destino e clique em **Importar Recorder**;
2. selecione o `.rpablockly.zip` e revise origem, ações, issues e evidências;
3. mapeie toda referência `input.recorded.*`, `secret.recorded.*` e
   `attachments.recorded.*` para uma raiz permitida;
4. escolha exatamente um modo: substituir, acrescentar ao principal ou criar
   subflow;
5. autorize remapeamento apenas depois de revisar as colisões;
6. valide, aplique e reabra a revisão publicada.

As amostras ajudam na revisão, mas não são publicadas como dados operacionais.
O bundle original e os mappings ficam como evidência lateral da revisão.

## Evoluir a extensão

Uma mudança de ação gravável deve manter juntos:

- tipo e normalização do evento;
- geração de `Action` V2 e requisitos de entrada;
- autoria de locator sem seletor na ação;
- schemas e tipos gerados, se o contrato mudar;
- validação TypeScript e C#;
- importador, Blockly e handler do runtime;
- fixtures unitárias, contrato cruzado e E2E.

Não adicione código remoto, permissão permanente ampla, `eval`, segredo em
checkpoint, HTML de página no slideshow ou seletor de negócio no fluxo.

## Produzir a release

```powershell
npm run licenses --prefix src/RpaFlow.Recorder.Extension
npm run release --prefix src/RpaFlow.Recorder.Extension
.\tools\Test-Dependencies.ps1
.\tools\Generate-Sbom.ps1
```

`release.mjs` compila duas vezes e só grava o ZIP se ambos forem idênticos byte a
byte. O checksum versionado fica em `src/RpaFlow.Recorder.Extension/release/` e o
ZIP ignorado pelo Git fica em `artifacts/`.

Arquitetura e decisões: [ADRs do Recorder](../adr/README.md) e
[threat model](threat-model.md).
