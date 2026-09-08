# Sidecar Cloudflare experimental

Este perfil executa Byparr 3.0.4 ou FlareSolverr 3.5.0 como processo isolado.
Ele atende somente Cloudflare Managed Challenge em alvos próprios ou cuja
automação foi autorizada. Não é usado para Turnstile e não constitui suporte
universal a Cloudflare.

Escolha exatamente um provider:

```powershell
.\rpablockly.cmd sidecar-up -SidecarProvider byparr
.\rpablockly.cmd sidecar-up -SidecarProvider flaresolverr

docker compose -f services/cloudflare-sidecar/compose.yaml `
  --profile byparr up -d byparr

docker compose -f services/cloudflare-sidecar/compose.yaml `
  --profile flaresolverr up -d flaresolverr
```

As imagens estão fixadas por digest e a porta é publicada somente em loopback.
As APIs nativas não possuem autenticação suficiente e funcionam como browsers
capazes de buscar URLs arbitrárias. Nunca publique a porta na Internet. Para uso
em outra máquina, coloque o sidecar atrás de mTLS ou proxy autenticado, aplique
allowlist e bloqueie loopback, redes privadas, link-local e metadata no egress.

Os digests do Compose são defaults reproduzíveis, não versões imutáveis do
produto. Para homologar uma atualização sem editar o arquivo:

```powershell
$env:BYPARR_IMAGE = "ghcr.io/thephaseless/byparr:<versão>@sha256:<digest>"
$env:FLARESOLVERR_IMAGE = "ghcr.io/flaresolverr/flaresolverr:<versão>@sha256:<digest>"
docker compose -f services/cloudflare-sidecar/compose.yaml `
  --profile byparr config
```

Promova uma referência somente depois de validar o contrato `/v1`; uma versão
nova do container pode alterar o envelope mesmo quando o Compose continua
válido. Evite tags flutuantes como `latest`.

O runtime chama `POST /v1`, aceita exclusivamente um `cf_clearance` cujo domínio
esteja em `CloudflareSidecarAllowedHosts`, aplica o cookie ao `BrowserContext`
original e recarrega a URL atual. Outros cookies, HTML, headers, screenshot e
tokens são descartados. O resultado permanece `InteractionDone`; somente a
pós-condição do fluxo pode promovê-lo a `Solved`.

Byparr transfere artefatos de uma sessão Firefox descartável. FlareSolverr usa
Chromium e pode manter sessões próprias, mas o RpaBlockly não importa nem expõe
esses IDs. Nenhum dos dois transfere a identidade completa para um contexto
Playwright existente; por isso a eficácia precisa ser homologada por domínio.

Licenças: Byparr é GPL-3.0; FlareSolverr é MIT. Revise as obrigações antes de
redistribuir imagens ou modificações.
