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

## Importar uma gravação

O botão **Importar Recorder** abre um wizard de cinco etapas: selecionar, revisar,
mapear, confirmar e aplicar. O backend inspeciona o ZIP sem extraí-lo, mantém um
staging isolado e exige token próprio. Preview e validate são somente leitura.

Antes do apply, mapeie referências `input.recorded.*`, `secret.recorded.*` e
`attachments.recorded.*`. Escolha substituir, acrescentar ao principal ou criar
um subflow. A publicação usa a revisão esperada; o bundle e os mappings ficam
arquivados como evidência lateral.

## Homologação assistida

O botão **Validar roteiro** executa o rascunho atual sem publicá-lo. O backend
valida flow, locators e policy, cria um `RpaPackageSnapshot` temporário e usa o
mesmo `PlaywrightV2FlowExecutor` da execução operacional.

Antes de iniciar:

1. escolha `CloakBrowser` ou `Chromium Playwright`;
2. escolha a última ação-folha que pode ser executada com segurança;
3. confirme explicitamente o limite;
4. mantenha screenshots habilitadas quando o conteúdo puder ser armazenado com
   a política de privacidade do ambiente.

A janela do navegador é sempre visível. O painel mostra o bloco ativo, cards por
etapa, falha estruturada, botão **Parar agora** e capturas sanitizadas. A execução
termina imediatamente depois da ação escolhida; terminar o fluxo sem alcançá-la
é falha. Somente uma homologação pode ficar ativa por sessão do editor.

O modo assistido:

- lê `Input`, `Attachments` e `Blockly.Variables` da configuração local;
- nunca salva storage state, pacote, resultados ou aprendizado;
- desabilita promoção e write-back no snapshot temporário;
- guarda imagens por sete dias sob `artifacts/homologacao-editor`, pasta ignorada
  pelo Git;
- não consulta fila nem banco e não substitui os intertravamentos do worker.

## Referências

- [guia operacional do editor](../../docs/referencia-markdown/guia-editor-blockly.md);
- [contrato do pacote V2](../../docs/referencia-markdown/pacote-schema-v2.md);
- [catálogo de blocos](../../docs/referencia-markdown/catalogo-de-blocos.md);
- [como adicionar um bloco](../../docs/referencia-markdown/como-adicionar-bloco.md);
- [solução de problemas](../../docs/referencia-markdown/tutorial-solucao-problemas-rpa-blockly.md).
- [manual do desenvolvedor do Recorder](../../docs/recorder/manual-desenvolvedor.md).
