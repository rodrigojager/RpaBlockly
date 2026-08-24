# Pacote operacional V2

## Unidade de publicação

Um pacote é a combinação inseparável de:

- `flow.production.json`, schema 2;
- `locators.production.json`, schema 1 do catálogo;
- `rpa.policy.json`, schema 1 da policy.

Os JSON Schemas oficiais ficam em `schemas/`. Propriedades desconhecidas são
recusadas. Arquivos usam UTF-8 sem BOM e LF; o hash usa JSON canônico sem espaços,
com chaves ordenadas e arrays preservados.

## Fluxo

O fluxo contém ações, inputs e subflows. Uma ação web referencia um locator:

```json
{
  "id": "submit",
  "type": "click",
  "name": "Enviar",
  "target": {
    "locatorId": "submit-button",
    "cardinality": "single"
  }
}
```

Cardinalidades:

- `single`: exige exatamente um alvo;
- `first`: aceita vários, materializa o primeiro deliberadamente;
- `many`: coleção; obrigatória para `readElements` e `typeAcrossInputs`.

Papéis possíveis: `target`, `trigger`, `options`, `ready`, `success`, `protocol`
e `condition.locator`.

## Catálogo de localizadores

Cada locator tem ID, nome amigável, candidatos ordenados e fingerprints. A ordem
do array é a prioridade efetiva. Uma receita aplica, nesta ordem:

1. frames do externo para o interno;
2. scope e seu filtro de texto;
3. target e seu filtro de texto.

Estratégias: `css`, `xpath`, `role`, `label`, `placeholder`, `text`, `testId`,
`rawPlaywright` e `fingerprint`. `rawPlaywright` existe para preservar expressões
históricas sem reinterpretá-las. Fingerprints nunca incluem `value`, senha,
token, cookie, autorização ou outro atributo sensível.

Origens de candidato:

- `developer`: `original` ou `alternative`;
- `recorder`: `capturedPrimary` ou `capturedAlternative`;
- `heuristic`: candidato aprendido, sem papel de autoria.

## Policy

`strict` usa somente o candidato 0. `fallback` percorre receitas exatas. `adaptive`
permite heurística apenas depois das receitas exatas e exige threshold, diferença
para o segundo colocado, cardinalidade e estado válidos.

Write-back: `disabled`, `memory`, `source` ou `overlay`. Promoção só ocorre após
resultado final `Succeeded`; `Validated`, falha, retry, cancelamento ou
encerramento inesperado descartam a sessão provisória.

## Layout do file store

```text
package-store/
  meu-rpa/
    current.json
    .package.lock
    revisions/
      <sha256>/
        flow.production.json
        locators.production.json
        rpa.policy.json
```

Não edite `revisions/` manualmente. Publique por `IRpaPackageWriter` ou pelo editor.
O ponteiro atual só muda depois de staging, validação e publicação integral.

## Limites

- até 1.000.000 de ações estruturais e 32 níveis;
- até 10.000 locators;
- até 100 candidatos por locator;
- até 16 frames por receita;
- até 20 fingerprints por locator;
- limites adicionais de texto, atributos, ancestrais, irmãos e tamanho dos três
  documentos em `RpaPackageLimits` e validadores.
