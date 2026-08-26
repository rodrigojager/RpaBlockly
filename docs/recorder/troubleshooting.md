# Solução de problemas — Recorder V2

## A extensão não abre

Confirme Chrome 116 ou superior, recarregue a extensão em
`chrome://extensions` e verifique se a pasta selecionada contém `manifest.json`.
Páginas internas como `chrome://` e `file://` não são graváveis.

## Nenhum passo aparece

Abra a página HTTP(S), clique no ícone do Recorder nessa própria aba, aceite o
aviso de privacidade e inicie. Aceite o pedido nativo de acesso às páginas
HTTP(S); ele permite injetar o capturador e gerar evidências ao atravessar sites
sem novo clique. Se a autorização tiver sido revogada, use **Restabelecer acesso
amplo** e depois **Retomar**.
Eventos sintéticos enviados pela própria página são ignorados. Um iframe sem uma
cadeia validável de locators vira pendência explícita.

## Acesso amplo precisa ser concedido novamente

Esse aviso aparece quando o Chrome revoga a autorização ou impede a injeção. A
sessão é pausada imediatamente para não perder ações em silêncio. Clique em
**Restabelecer acesso amplo**, aceite o aviso do Chrome e só então use
**Retomar**. Uma simples mudança entre sites não deve mostrar esse aviso na RC 9.

## Há passos duplicados ou ausentes

Pause e retome a sessão para forçar a leitura do checkpoint. Digitação contínua,
`change` final e submit causal são coalescidos em ações executáveis. Widgets
customizados, shadow root fechado, alvos ambíguos e ações sem bloco aparecem como
pendências bloqueantes. A descrição informa se é defeito de locator, ampliação de
bloco existente ou candidato a bloco novo.

## Screenshot ou upload não entra no ZIP

Confira as opções da sessão e o indicador **Evidências visuais** no topo do
painel. Ele informa quantas imagens foram realmente salvas e mostra se a falha
ocorreu na preparação, captura, processamento ou gravação local. Na RC 9, aceite
o pedido amplo de páginas HTTP(S). Recarregue a extensão atualizada e inicie uma nova sessão com
**Capturar evidências visuais** marcado. Screenshots obedecem rate limit e
quantidade máxima.
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
