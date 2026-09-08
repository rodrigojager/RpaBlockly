# ADR-020 — Sidecar Cloudflare por transferência de artefato

Estado: Aceita como capacidade experimental

## Contexto

Cloudflare Managed Challenge pode exigir uma sessão de navegador que o processo
Playwright original não consegue concluir passivamente. Byparr e FlareSolverr
resolvem a navegação em browsers próprios e devolvem cookies, mas nenhum deles
oferece uma conexão que preserve integralmente o `BrowserContext` do chamador.
Copiar cookies não transfere engine, proxy, TLS, client hints ou fingerprint.

## Decisão

O runtime aceita os contratos nativos de Byparr 3.0.4 e FlareSolverr 3.5.0 por
um cliente provider-neutral interno. A capacidade exige três autorizações:

- `CloudflareSidecarEnabled=true` na implantação;
- domínio explícito em `CloudflareSidecarAllowedHosts`;
- `captcha.kind=cloudflareChallenge` e `allowCloudflareSidecar=true` na ação.

O alvo deve usar HTTPS. O cliente não envia cookies da sessão original, não
aceita redirects no endpoint e limita timeout e tamanho da resposta. Somente um
`cf_clearance` com domínio/path/expiração/flags válidos é importado; os demais
cookies e todo HTML, screenshot, header ou token são descartados.

Depois da importação, o runtime recarrega a URL atual e executa novamente o
detector no mesmo contexto. Desaparecer o marcador produz `InteractionDone`, não
`Solved`. A promoção para `Solved` continua dependendo da pós-condição explícita
do fluxo. Falha de contrato, challenge remanescente ou sidecar indisponível segue
para o handoff humano.

## Segurança operacional

Os providers são serviços de browser sem autorização forte e podem se tornar um
proxy SSRF. Os containers ficam em perfil separado, porta em loopback, imagem
com default por digest, capabilities removidas e logs reduzidos. As referências
podem ser sobrescritas por variável de ambiente para homologar atualizações, mas
continuam obrigadas operacionalmente a usar digest e passar pelos checks de
contrato. Uso remoto exige proxy com mTLS
ou identidade de serviço, allowlist e política de egress que bloqueie redes
privadas, link-local, metadata e DNS rebinding. Cookies e credenciais não entram
em logs, resultados, corpus ou pacote Blockly.

## Alternativas recusadas

- Tratar retorno `status=ok` como solução: não prova continuidade no navegador.
- Importar todos os cookies: mistura estado autenticado e amplia o impacto.
- Usar o sidecar para Turnstile: o runtime não produz nem injeta token de widget.
- Expor sessão FlareSolverr ao fluxo: acopla ownership e não permite Playwright
  controlar diretamente o browser do provider.
- Incorporar o código dos providers: aumenta acoplamento e obrigações de licença.

## Consequências

A transferência pode falhar quando User-Agent, engine, proxy ou fingerprint não
coincidem. Essa limitação é registrada no resultado e deve ser homologada em cada
domínio autorizado. O reload também é um efeito explícito do opt-in da ação.

## Rollback

Definir `CloudflareSidecarEnabled=false` remove o caminho sem alterar contratos
anteriores. O adapter same-page e o handoff humano permanecem disponíveis.

## Testes e evidências

`RpaFlow.PlaywrightChecks` cobre filtragem de cookies, domínio hostil e aplicação
do clearance seguida de reload em um `BrowserContext` interceptado. O teste não
simula eficácia contra um serviço Cloudflare real.
