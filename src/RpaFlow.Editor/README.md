# Editor Blockly V2

Aplicação ASP.NET Core local que serve o Blockly sem CDN e abre um pacote V2
revisionado. O editor manipula fluxo, catálogo de localizadores e política como
uma unidade; blocos web armazenam somente `locatorId` e cardinalidade.

## Perfil do projeto

Cada RPA possui um `rpa.editor.json` com:

- nome exibido e arquivo `.csproj`;
- arquivo de configuração editável;
- `rpaId` e raiz do `package-store`;
- campos de configuração liberados no painel.

O pacote aberto contém:

- `flow.production.json`, schema 2;
- `locators.production.json`, schema 1;
- `rpa.policy.json`, schema 1.

O drawer de localizadores pesquisa por ID e nome amigável, exibe candidatos,
receitas, fingerprints, origem, papéis e ordem. O drawer de política controla os
modos `strict`, `fallback` e `adaptive`, além da promoção e do write-back.

## Execução

Na raiz do repositório:

```powershell
dotnet run --project src/RpaFlow.Editor/RpaFlow.Editor.csproj -- --project-root C:\caminho\do\RPA
```

Opções:

- `--no-open`: não abre o navegador automaticamente;
- `--url http://127.0.0.1:5187`: altera a porta, mantendo loopback;
- `--configuration <caminho>`: substitui o arquivo de configuração do perfil.

O servidor aceita somente loopback, não habilita CORS e exige o token aleatório
da sessão em toda API protegida. Caminhos que escapem da pasta do RPA, JSON
inválido e bytes que não sejam UTF-8 são recusados.

## Concorrência e persistência

Salvar o pacote exige a revisão esperada. O backend valida os três documentos,
grava em staging e publica uma nova revisão por compare-and-swap. Em conflito,
o editor oferece recarregar/comparar; nunca sobrescreve silenciosamente a
revisão concorrente. A configuração do host é um documento separado do pacote.

As APIs `/api/package`, `/api/flow`, `/api/locators` e `/api/policy` sempre
publicam um pacote completo e consistente. `/api/package/revisions` expõe o
histórico; `/api/configuration` edita somente os campos autorizados pelo perfil.

## Referências

- [guia operacional do editor](../../docs/referencia-markdown/guia-editor-blockly.md);
- [contrato do pacote V2](../../docs/referencia-markdown/pacote-schema-v2.md);
- [catálogo de blocos](../../docs/referencia-markdown/catalogo-de-blocos.md);
- [como adicionar um bloco](../../docs/referencia-markdown/como-adicionar-bloco.md);
- [solução de problemas](../../docs/referencia-markdown/tutorial-solucao-problemas-rpa-blockly.md).
