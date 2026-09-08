# ADR-022 — SpyBrowser como navegador padrão

Estado: Aceita

## Contexto

O runtime usava CloakBrowser como padrão do worker e dos exemplos, enquanto
hosts sem configuração caíam em Chromium Playwright. Essa divergência dificultava
reproduzir a homologação e mantinha as interações operacionais sem a cadência
humana opcional oferecida pelo SpyBrowser. O SpyBrowser `0.2.0-beta.1` usa as
interfaces oficiais do Microsoft.Playwright e pode criar contextos configurados
sem exigir alterações nos blocos, locators ou contratos de pacote.

## Decisão

`spybrowser` passa a ser o valor padrão de `Runtime.Browser` no worker, template,
exemplo, checks de runtime e homologação assistida. Chromium, Firefox, WebKit,
canais Chrome/Edge e CloakBrowser continuam aceitos explicitamente.

O RpaBlockly usa `SpyBrowserLauncher.LaunchBrowserAsync` com uma identidade em
memória, engine Chromium e um contexto descartável por execução. O contexto é
criado pelo handle do SpyBrowser para receber identidade e proxies humanizados,
mas continua sendo um `IBrowserContext`; todo o restante do runtime usa
`IPage`, `ILocator` e demais interfaces oficiais sem conhecer o provider.
`Runtime.SpyBrowserHumanize` controla a humanização e nasce como `true`.

Perfis persistentes do SpyBrowser não são usados. O worker escolhe e reserva um
caso, e a execução continua isolada em contexto novo; quando necessário, o
estado autenticado explícito permanece no mecanismo existente de storage state.
Isso evita compartilhar cookies ou histórico implicitamente entre casos.

## Confiabilidade e segurança

SpyBrowser não é tratado como mecanismo de bypass, anonimização ou stealth
completo. A integração usa o Chromium provisionado pelo Playwright e não aceita
paths de executável, argumentos, proxy ou driver vindos do fluxo Blockly. O
pacote NuGet é fixado em `0.2.0-beta.1`.

Atalhos compostos, como `Control+A`, recebem opções explícitas e delegam ao
Playwright bruto porque a humanização beta interpreta apenas teclas simples.
Cliques e teclas simples suportados permanecem humanizados. Preenchimentos usam
Playwright bruto com timeout e encerramento do contexto no cancelamento porque a
API humanizada beta não recebe o token da execução; `typeSequentially` mantém a
cadência configurada pelo próprio bloco. Gestos de captcha que já possuem
cadência própria também usam objetos Playwright brutos para impedir humanização
duplicada. Chamadas com opções específicas e frame locators podem continuar no
caminho bruto.

A identidade transitória usa o timezone local do host, convertido para IANA no
Windows, em vez do default regional do pacote. Assim a troca de provider não
altera silenciosamente a semântica temporal observada pelo fluxo.

## Alternativas recusadas

- Substituir o pacote CloakBrowser pela facade de compatibilidade: impediria
  manter CloakBrowser como opção porque ambas as bibliotecas produzem um
  assembly chamado `CloakBrowser.dll`.
- Usar perfil persistente por padrão: misturaria estado entre casos e mudaria a
  autoridade atual do storage state.
- Remover os providers anteriores: eliminaria rollback e engines necessárias a
  fluxos já existentes.
- Colocar o provider no schema do pacote: navegador é configuração operacional
  do host, não semântica do fluxo Blockly.

## Consequências

O browser Chromium do Playwright precisa estar provisionado também quando o
valor é `spybrowser`. A cadência humana aumenta a duração de algumas ações; o
operador pode definir `SpyBrowserHumanize=false` ou selecionar `chromium` para
execução bruta. Atualizações do pacote beta exigem regressão completa antes de
alterar o pino.

## Rollback

Definir `Runtime.Browser` como `chromium`, `cloakbrowser`, Firefox, WebKit ou um
canal Chrome/Edge restaura o launcher anterior sem alterar o pacote RPA. Remover
o branch `spybrowser` e a referência NuGet devolve o default anterior.

## Testes e evidências

`RpaFlow.PlaywrightChecks` executa por padrão o fluxo diferencial completo com
SpyBrowser humanizado, cobrindo formulário, atalho, popup, iframes, storage state
e captchas. `Rpa.WorkerChecks` fixa o default e preserva as opções anteriores.
`RpaFlow.EditorRoundTrip` valida o dropdown e inicia uma homologação real com
SpyBrowser. O repositório do SpyBrowser passou seus 38 testes antes da integração.
