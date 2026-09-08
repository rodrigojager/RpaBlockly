# captcha-solver

Serviço self-hosted e opcional que isola inferência do worker. OCR de imagem,
áudio do reCAPTCHA v2 e classificação binária de grades hCaptcha rodam
localmente em CPU. Tarefas visuais podem usar um VLM opcional por um router
LiteLLM separado. O .NET preserva a sessão Playwright, valida o snapshot e é o
único componente que executa cliques ou arrastes.

## Contrato

`POST /solve` com um corpo JSON `{ "type": "<tipo>", ...payload }`.

| `type` | payload | resposta (`success=true`) |
| --- | --- | --- |
| `image` | `{ "imageBase64": "<png-base64>" }` | `{ "text": "a3x9" }` |
| `recaptcha_v2_audio` | `{ "audioBase64": "<mp3-base64>" }` | `{ "text": "7 4 2 1" }` |

O envelope V2 também aceita `type: "hcaptcha_image_label"` com
`assets.tilesBase64` (um PNG/JPEG por tile da grade), `hint` com o prompt do
desafio e nenhum `task`. A resposta informa `tiles`: uma decisão
`{ "index", "match", "confidence" }` por tile, na ordem enviada. O rótulo do
prompt é resolvido por aliases (en/zh/pt) do manifesto fixado; rótulos sem
modelo provisionado devolvem `model_missing` com sugestão `human_handoff`,
cabendo ao .NET o fallback VLM ou a intervenção humana. Somente a modalidade
de grade binária (`image_label_binary`) é coberta; point, drag e múltipla
escolha seguem os caminhos já existentes.

O envelope V2 também aceita `type: "visual"`, `provider`, `variant`, `task`,
`hint`, geometria e uma captura em `assets.imageBase64`. As tarefas permitidas
são `text`, `grid`, `point`, `bounding_box`, `slider`, `rotation` e `drag`.
O resultado contém apenas ações tipadas `TypeText`, `Click` ou `Drag`, em pixels
da captura. Coordenadas não finitas ou fora da imagem são recusadas.
`options.maxAttempts` limita tentativas retryable dentro do mesmo `deadlineUtc`,
e cada inferência OCR, áudio ou VLM consome uma unidade. A resposta V2 informa a
quantidade efetivamente usada em `attempts`. O cliente
.NET não reenvia a requisição V2: sem deduplicação server-side, até uma falha de
transporte poderia repetir inferências já consumidas.

Falhas úteis devolvem `{ "success": false, "error": "..." }`. Tipos não
suportados devolvem `unsupported_type` com sugestão `human_handoff`.

## Rodar local

```bash
pip install -r requirements.txt
$env:CAPTCHA_API_KEY = "troque-esta-chave"
uvicorn app:app --port 8855
```

## Rodar em Docker

Pelo launcher da raiz:

```powershell
.\rpablockly.cmd captcha-up
```

Ou diretamente pelo Docker:

```bash
docker build -f services/captcha-solver/Dockerfile -t captcha-solver .
docker run -d --name captcha-solver -p 127.0.0.1:8855:8855 \
  -e CAPTCHA_API_KEY=troque-esta-chave captcha-solver
```

## Apontar o RpaBlockly

```json
"Runtime": {
  "Captcha": {
    "ServiceUrl": "https://solver.interno.exemplo",
    "ServiceApiKey": "troque-esta-chave"
  }
}
```

HTTP sem TLS é aceito somente para loopback, como `http://127.0.0.1:8855`.
Serviços acessados por rede devem usar HTTPS para proteger o Bearer e os assets.

Sem `ServiceUrl` configurado, o OCR embutido do RpaBlockly cobre imagem/texto
e o `solveRecaptchaV2` falha pedindo a URL. O serviço exige
`CAPTCHA_API_KEY`; somente desenvolvimento estritamente local pode optar por
`CAPTCHA_ALLOW_UNAUTHENTICATED=1`. Cada asset decodificado é limitado a 5 MiB
para imagem ou áudio, com uma inferência simultânea por modelo. A imagem Docker
provisiona o ONNX, os classificadores hCaptcha e a revisão fixada do Whisper
`base` durante o build. Em execução, o Whisper usa somente o cache em `HF_HOME`
e nada é baixado em runtime.

## Grade hCaptcha

A capacidade `hcaptcha_image_label` usa classificadores ResNet ONNX do model
hub do hcaptcha-challenger (release `model`), fixados por SHA-256/tamanho em
`src/RpaFlow.Playwright/V2/Captcha/hcaptcha-models.manifest.json` e
provisionados pelo usuário — nunca baixados em runtime:

```powershell
./tools/Get-CaptchaModels.ps1 -Suite hcaptcha
```

Os modelos foram autorizados pelo autor do upstream para uso como CC0; o texto
da licença acompanha a cópia provisionada (`captcha-models/hcaptcha/LICENSE`).
Para cobrir um rótulo novo, adicione-o ao conjunto curado de
`tools/Update-HcaptchaModelManifest.py`, rode o script para refazer o manifesto
e provisione novamente.

Para evoluir os classificadores, altere o conjunto `CURATED` no script,
regenere o manifesto, provisione e rode os testes reais antes de versionar a
alteração:

```powershell
python tools/Update-HcaptchaModelManifest.py
.\tools\Get-CaptchaModels.ps1 -Suite hcaptcha -Force
$env:RPABLOCKLY_REQUIRE_CAPTCHA_MODELS = "1"
python -m pytest services/captcha-solver/tests
```

O Git recebe o código, o manifesto e `hcaptcha-models.LICENSE.txt`; os `.onnx`
continuam como cache derivado. Para um modelo ou mirror próprio,
`Get-CaptchaModels.ps1` aceita `-ManifestPath` e `-LicensePath`, e
`provision_models.py` aceita os caminhos equivalentes. O novo modelo precisa
preservar o contrato de tensores ou vir acompanhado da mudança correspondente
no pipeline de pré-processamento e inferência.

Variáveis do container de inferência:

```text
CAPTCHA_ENABLE_HCAPTCHA=1
CAPTCHA_HCAPTCHA_MODEL_DIR=/app/models/hcaptcha
CAPTCHA_HCAPTCHA_MANIFEST_PATH=/app/models/hcaptcha/hcaptcha-models.manifest.json
CAPTCHA_MAX_HCAPTCHA_TILES=16
```

## Fallback VLM com LiteLLM

O fallback fica desligado por padrão e cada requisição ainda precisa definir
`allowVlmFallback: true`. Se `localOnly: true`, o router só pode ser usado quando
`CAPTCHA_LITELLM_IS_LOCAL=1`.

Variáveis do container de inferência:

```text
CAPTCHA_ENABLE_VLM=1
CAPTCHA_LITELLM_URL=http://litellm:4000
CAPTCHA_LITELLM_API_KEY=<chave interna do router>
CAPTCHA_LITELLM_MODEL=captcha-vision
CAPTCHA_LITELLM_IS_LOCAL=0
CAPTCHA_VLM_TIMEOUT_SECONDS=45
CAPTCHA_VLM_MAX_ACTIONS=25
```

O `compose.yaml` base não carrega o LiteLLM nem exige variáveis VLM. O override
`compose.vlm.yaml` adiciona o router e exige que `LITELLM_IMAGE` seja uma imagem
fixada por tag e digest; não usa `latest`. O provider real é configurado em
`CAPTCHA_VLM_UPSTREAM_MODEL`, permitindo trocar modelo/provider sem alterar o
RpaBlockly. Nenhuma chave real deve entrar no compose ou no pacote do fluxo.

```bash
docker compose -f services/captcha-solver/compose.yaml \
  -f services/captcha-solver/compose.vlm.yaml up -d
```

Para um modelo servido localmente pelo LiteLLM, marque
`CAPTCHA_LITELLM_IS_LOCAL=1`. Para providers remotos, mantenha `0` e envie
`localOnly: false` explicitamente. `/ready` também consulta a readiness do router.

## Corpus e métricas

O benchmark versionado gera 200 imagens, 50 áudios e 30 cenários geométricos sem
versionar binários. Ele exporta JSON/CSV e diferencia capacidade avaliada de
modelo ou gerador indisponível. Consulte [`benchmarks`](benchmarks/README.md).
