# Microservidor e editor compartilhado

Aplicação ASP.NET Core local que serve o Blockly, carrega o perfil do RPA e persiste configuração e fluxo. Seu código-fonte e a biblioteca Blockly 13.1.1 ficam nesta pasta; não há CDN.

Documentação relacionada:

- [guia operacional do editor](../docs/guia-editor-blockly.md);
- [schema JSON versão 1](../docs/flow-schema-v1.md);
- [catálogo completo de blocos](../docs/catalogo-de-blocos.md);
- [checklist para adicionar ou alterar um bloco](../docs/como-adicionar-bloco.md).

Cada RPA deve possuir um `rpa.editor.json` com:

- nome exibido no editor;
- arquivo `.csproj` usado para reconhecer o projeto;
- nomes da configuração e do fluxo;
- campos da configuração que o painel poderá editar e ligar aos dados daquele RPA.

O editor não contém campos ou fluxos predefinidos de um sistema específico. Ao conectar, ele usa os campos do perfil e transforma o `flow.production.json` daquele RPA em blocos. O backend valida o fluxo pelo mesmo `RpaFlow.Contracts` usado em produção.

Localizadores podem receber `frameSelectors`, editado como lista JSON do iframe externo para o interno. `hasTextSource` torna o texto do alvo dinâmico; `scopeHasText` e `scopeHasTextSource` filtram um escopo antes de localizar o elemento interno. Texto literal e origem são exclusivos. O bloco `transformar caminho` obtém nome, nome sem extensão, extensão ou pasta de caminhos locais e UNC. Todas essas propriedades participam do round-trip e não exigem código específico por portal.

O tipo de campo de configuração `stringList` edita uma lista JSON de textos. Ele é usado por `Runtime.BusySelectors`, permitindo adaptar os indicadores de loading de cada portal sem recompilar. Esperas e condições expõem cardinalidade `single`/`first`; as propriedades representadas pelos blocos e cobertas pela fixture generalizada são verificadas no round-trip. Uma propriedade aceita pelo schema, mas ainda não exposta pelo bloco correspondente — por exemplo, `navigate.timeoutMs` — pode ser perdida ao importar e reserializar. Confira o JSON ao vivo e o [guia operacional](../docs/guia-editor-blockly.md) antes de salvar.

Condições por valor aceitam caminho, texto literal ou JSON literal, preservando número, booleano, nulo, array e objeto. `repeat` permite nomear o índice em `loop.*`; `forEach` mantém escopos aninhados; chamadas de subfluxo são validadas contra referências ausentes, ciclos e profundidade excessiva.

A toolbox é uma biblioteca única e completa para todos os RPAs. Um projeto se torna particular pelo `flow.production.json`, pelos dados/configurações e, quando necessário, por um adaptador técnico como uma política de confirmação segura. Metadados de capability descrevem dependências consultáveis, mas o host atual não executa auditoria prévia nem enforcement automático com base neles. Eles não escondem blocos por RPA, não concedem autorização e não substituem o handler ou a política exigida; os perfis atuais do editor também não declaram uma lista de capabilities por RPA.

No bloco de confirmação final, a caixa **comprovar conclusão e publicar feedback** controla somente a serialização atômica dos critérios de mensagem, protocolo e destinos `runtime.*`. Ela não autoriza o efeito final; essa decisão continua protegida no host e na política específica do portal.

Para executar a partir da raiz do workspace:

```powershell
dotnet run --project src/RpaFlow.Editor/RpaFlow.Editor.csproj -- --project-root C:\caminho\da\base\rpas\MeuRpa
```

Opções adicionais:

- `--no-open`: não abre o navegador automaticamente;
- `--url http://127.0.0.1:5187`: altera a porta, mantendo o endereço local;
- `--configuration <caminho>`: substitui o arquivo definido no perfil;
- `--flow <caminho>`: substitui o fluxo definido no perfil.

O servidor aceita apenas conexões de loopback, não habilita CORS e exige um token aleatório nas APIs de leitura e gravação. Antes de substituir um arquivo, salva a versão anterior como `.bak`. Existe apenas um backup por arquivo, e o botão **Restaurar fluxo salvo** não usa esse `.bak`: ele restaura a fotografia carregada no início da sessão. JSON inválido, bytes que não sejam UTF-8 e caminhos do perfil que escapem da pasta do RPA são recusados.

**Salvar tudo** grava primeiro a configuração e depois o fluxo; não é uma transação única. **Importar fluxo de produção** e **Importar workspace** limpam o workspace antes de terminar e não possuem rollback. Exporte o workspace e preserve o baseline antes de uma importação incerta.

Consulte também o [tutorial de solução de problemas](../docs/tutorial-solucao-problemas-rpa-blockly.md), consolidado a partir das falhas já observadas durante a criação e a validação dos RPAs.
