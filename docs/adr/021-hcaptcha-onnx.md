# ADR-021 — hCaptcha por classificadores ONNX provisionados

Estado: Aceita

## Contexto

hCaptcha é a modalidade de captcha mais comum depois do reCAPTCHA. O runtime já
detectava o widget, mas toda superfície caía no fallback VLM (quando
habilitado) ou na intervenção humana. O projeto QIN2DIM/hcaptcha-challenger
publica um model hub com classificadores ResNet ONNX binários (~300 KB por
rótulo) treinados para a modalidade `image_label_binary`, a mais frequente do
hCaptcha. O código-fonte do upstream é GPL-3.0 e orienta um agente próprio com
navegador embutido — incompatível com a fronteira do RpaBlockly, onde o .NET é
o único componente que interage com a página.

## Decisão

O serviço `captcha-solver` ganha a capacidade `hcaptcha_image_label`: recebe os
tiles da grade e o prompt, resolve o rótulo por aliases (en/zh/pt) do
manifesto fixado e devolve uma decisão `{ index, match, confidence }` por tile.
O .NET clica os elementos correspondentes e submete; `Solved` só sai da
transição observável do checkbox (`#checkbox[aria-checked='true']`) ou da
pós-condição explícita do fluxo.

Os classificadores são provisionados pelo usuário a partir do model hub do
upstream, fixados por SHA-256/tamanho em `hcaptcha-models.manifest.json`
(suíte `hcaptcha-resnet-binary`, 34 rótulos curados). Nenhum download acontece
em runtime e nenhum código do upstream é incorporado: o pré-processamento e a
inferência foram reescritos sobre PIL/numpy/onnxruntime, já dependências do
serviço, e o contrato de tensores (`float32[1,3,64,64] → float32[1,2]`,
índice 0 positivo) é validado no primeiro carregamento. O autor do upstream
autorizou o uso dos modelos como CC0 conforme registro do mantenedor; o texto
da licença acompanha a cópia provisionada.

A nova ação `solveHCaptcha` exige `Captcha.ServiceUrl`, como
`solveRecaptchaV2`. No `solveCaptcha`, o solver dedicado é tentado primeiro;
superfícies não-binárias (point, drag, múltipla escolha) e rótulos sem modelo
provisionado (`model_missing`) caem no caminho anterior — fallback VLM quando
`AllowVlmFallback`, senão intervenção humana. Falhas de verificação após o
orçamento são finais e não disparam o fallback.

## Segurança operacional

O redirect 302 dos assets do GitHub para `objects.githubusercontent.com` ou
`release-assets.githubusercontent.com` é aceito somente no provisionamento,
com integridade garantida pelo SHA-256 fixado; qualquer outro host é recusado.
Cada tile é limitado a 5 MiB e a grade a 16 tiles por requisição, dentro do
orçamento de corpo do serviço. A inferência é serializada por capacidade e
responde `busy` com `Retry-After` quando ocupada. Prompt, tiles e decisões não
entram em logs, resultados, corpus ou pacote Blockly; o resultado da ação
expõe apenas estado, solver e evidência da pós-condição.

## Alternativas recusadas

- Incorporar o pacote `hcaptcha-challenger`: além das obrigações da GPL-3.0
  sobre código derivado, o pacote assume o controle do navegador, rompendo a
  fronteira que mantém o .NET como único executor de cliques.
- Baixar modelos sob demanda em runtime: fere a regra de provisionamento
  fixado e introduz supply chain não auditável no caminho quente.
- Cobrir point/drag/segmentação na v1: exigiria modelos YOLO de dezenas de MB
  por família de prompt e NMS próprio; o fallback VLM/humano já cobre essas
  superfícies sem ampliar a superfície de inferência.
- Portar o denoise NLM de tiles 144 px com marca d'água: traria OpenCV como
  dependência pesada para um único filtro. Limitação registrada: desafios com
  marca d'água podem perder precisão; o fallback cobre o insucesso.

## Consequências

Rótulos fora do conjunto curado retornam `model_missing` com sugestão
`human_handoff`; cobrir um rótulo novo exige regenerar o manifesto
(`tools/Update-HcaptchaModelManifest.py`) e provisionar de novo. O contrato V2
ganha o campo `tiles` na resposta, retrocompatível: clientes antigos ignoram o
campo e serviços antigos rejeitam o tipo como `unsupported_type`.

## Rollback

Definir `CAPTCHA_ENABLE_HCAPTCHA=0` remove a capacidade do serviço; remover a
ação `solveHCaptcha` do pacote devolve o hCaptcha ao fallback VLM/humano sem
alterar contratos anteriores.

## Testes e evidências

`tests/test_hcaptcha.py` cobre envelope, aliases em três idiomas, homóglifos,
erros estáveis e inferência real dos modelos fixados (CI com
`RPABLOCKLY_REQUIRE_CAPTCHA_MODELS=1`). `RpaFlow.PlaywrightChecks` cobre o
parse defensivo de `tiles` no cliente e o fluxo checkbox → grade → submit em
página-fixture servida por `page.RouteAsync`, incluindo o sinal `Unsupported`
em superfície não-binária. Nenhum teste simula eficácia contra um hCaptcha
real; a precisão dos rótulos provisionados deve ser homologada por domínio.
