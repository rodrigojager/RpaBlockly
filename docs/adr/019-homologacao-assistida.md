# ADR-019 — Homologação assistida no editor

Estado: Aceita

## Contexto

Depois de importar ou editar um roteiro, uma pessoa precisa observar a execução
real antes de entregar o pacote para integração com worker e banco. Executar o
workspace Blockly criaria um segundo runtime e poderia divergir da produção.

## Decisão

O editor cria um `RpaPackageSnapshot` temporário a partir do rascunho e executa o
mesmo `PlaywrightV2FlowExecutor` usado pelo host e pelo worker. A pessoa escolhe
Chromium ou CloakBrowser e uma última ação-folha segura, inclusiva. O guard encerra
a execução imediatamente depois dessa ação.

O modo assistido é local, exige token da sessão, permite somente uma execução
simultânea e propaga cancelamento. O snapshot desabilita promoção e write-back de
aprendizado. Nenhum pacote, storage state, output ou dado de fila é persistido.

Screenshots por etapa são opcionais e sanitizadas antes da gravação. O frontend
recebe apenas eventos técnicos e IDs de evidência; caminhos físicos e valores do
caso não saem do backend local.

## Alternativas recusadas

- Executar o workspace Blockly: criaria semântica paralela ao JSON V2.
- Chamar o projeto do RPA como subprocesso: perderia eventos por ação, cancelamento
  estruturado e associação segura das evidências.
- Executar o fluxo inteiro por padrão: poderia alcançar um efeito irreversível.
- Habilitar write-back adaptativo durante homologação: alteraria o pacote sem uma
  conclusão operacional confirmada.

## Consequências

O editor passa a depender do runtime Playwright, mas continua fora da produção.
O limite seguro precisa ser alcançado; um ramo não percorrido produz falha. A
execução pode usar configuração local e sessão autenticada existente, sem salvar
novo storage state.

## Rollback

Remover as APIs e o painel assistido não altera pacotes publicados. A opção de
captura por ação permanece desativada por padrão no runtime.

## Testes e evidências

`RpaFlow.EditorRoundTrip` cobre snapshot de rascunho, progresso, destaque,
screenshot, endpoint autenticado, limite seguro, falha e cancelamento.
