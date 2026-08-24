# Solução de problemas da V2

## O editor não abre o pacote

- confira `rpaId` e `packageStoreRoot` em `rpa.editor.json`;
- confirme `current.json` e a pasta apontada em `revisions/`;
- execute o host com `--validate-only` para obter o erro oficial;
- não edite hash, ponteiro ou documentos da revisão manualmente.

## Conflito ao salvar

Outra sessão publicou a partir da mesma revisão. Recarregue, compare ou reaplique
sua alteração sobre a nova revisão. O conflito é intencional; não remova o
`expectedRevision`.

## Locator ausente ou ambíguo

1. confira o `locatorId` do bloco;
2. abra o catálogo e revise a receita completa;
3. valide frames externos → internos, scope e target;
4. use `single` somente quando houver exatamente um alvo;
5. examine `resolucao.json` e os eventos de tentativa;
6. adicione alternativa exata antes de considerar adaptive.

Não mova selector para o bloco e não aumente timeout para ocultar ambiguidade.

## Heurística recusada

Empate, confiança baixa, diferença insuficiente, cardinalidade ou estado inválido
devem falhar com segurança. Ajuste fingerprint/fixtures e calibre thresholds; não
escolha apenas o maior score disponível.

## Artefato não foi salvo

Confira `OutputDirectory`, extensão, estratégia de conflito e os limites
`MaximumArtifactBytes`/`MaximumArtifactFilesPerExecution`. Pastas relativas não
podem escapar da raiz; caminhos absolutos precisam ser deliberados.

## Worker não faz claim

Valide `Enabled`, `ClaimEnabled`, conexão, nomes das tabelas, lease e código da
definição. Aplique as migrations em ordem. Para OTP, valide alias/provider e a
restrição operacional de paralelismo configurada.

## Checks rápidos

```powershell
dotnet build RpaBlockly.slnx --configuration Release
npm run check --prefix tools/schema-conformance
.\tools\Run-Checks.ps1
.\tools\Test-Dependencies.ps1
```

Se o Chromium do Playwright não estiver instalado:

```powershell
pwsh src/RpaFlow.Playwright/bin/Release/net9.0/playwright.ps1 install chromium
```
