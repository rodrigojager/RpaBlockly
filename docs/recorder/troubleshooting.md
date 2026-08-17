# Solução de problemas — Recorder V2

## A extensão não abre

Confirme Chrome 116 ou superior, recarregue a extensão em
`chrome://extensions` e verifique se a pasta selecionada contém `manifest.json`.
Páginas internas como `chrome://` e `file://` não são graváveis.

## Nenhum passo aparece

Inicie uma sessão, aceite o aviso de privacidade e conceda a origem solicitada.
Eventos sintéticos enviados pela própria página são ignorados. Em iframe de outra
origem, conceda também a origem do frame; caso contrário, a extensão cria uma
pendência explícita.

## Há passos duplicados ou ausentes

Pause e retome a sessão para forçar a leitura do checkpoint. Digitação contínua,
`change` final e submit causal são coalescidos. Widgets customizados, shadow root
fechado e alvos ambíguos podem ser omitidos com issue bloqueante; revise a lista
de pendências em vez de editar o JSON.

## Screenshot ou upload não entra no ZIP

Confira as opções da sessão. Screenshots obedecem rate limit e quantidade máxima.
Uploads grandes, tipos bloqueados ou não consentidos preservam somente metadados.
O caminho local exibido pelo navegador nunca é uma entrada válida do RPA.

## O download falha

Não cancele a sessão: ela permanece pausada para nova tentativa. Verifique a pasta
de downloads e as políticas do Chrome. A sessão só é limpa após confirmação do
download.

## O editor rejeita o ZIP

Use o ZIP original. Hash divergente, arquivo extra, duplicidade de caixa, Zip
Slip, symlink, razão de compressão excessiva e JSON fora do schema são rejeitados.
Confira também se o Recorder e o editor pertencem à mesma linha de release.

## Validate não permite Apply

Resolva issues bloqueantes e forneça mappings para todas as referências gravadas.
Se a revisão do pacote mudou desde o preview, reabra a importação; o editor não
sobrescreve uma revisão concorrente.

## O runtime falha em strict

Reproduza na fixture ou ambiente autorizado e examine os eventos
`locatorCandidateRejected`. Não altere seletores dentro de ações. Atualize o
catálogo de locators pelo editor e escolha `fallback` somente quando alternativas
exatas, auditadas e ordenadas estiverem disponíveis.
