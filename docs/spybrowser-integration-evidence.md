# Evidência da integração SpyBrowser

## Pacote público efetivamente consumido

Versão `0.2.0-beta.2.1`, fonte `7983680083dd66aea2ff39a753533d02d8de2491`. Os três hashes em `tools/spybrowser-packages.lock.json` foram conferidos após download público sem autenticação. Não há feed absoluto, binários de pacote nem credenciais versionados.

A release do SpyBrowser inclui resultados TRX: 179 testes de produto executados/passaram (um caso opcional headed não habilitado) e 37 nativos passaram. A auditoria de seus cinco PDBs confirmou 62 checksums de documentos reais baixados do SourceLink público, sem normalizar bytes. Isso não é garantia de GPU real, de sites/provedores arbitrários ou de p95 universal.

## Execução no consumidor

- SDK `10.0.401`, projetos .NET 9; TEMP/TMP e cache NuGet novo em D.
- Bootstrap público e restore completo em cache novo: sucesso.
- Build completo `RpaBlockly.slnx`: sucesso.
- Checks focados: 11 grupos de assertions de defaults, quatro seleções válidas, valores ativos inválidos, campos inativos e shape legado.
- Três casos reais em páginas locais: Cursory/PlaywrightCompatible ligado; Bézier/Legacy ligado; humanização desligada/raw. Wrapping, Fill, clique e movimento final conferidos.
- DLLs executadas do Core/Cursory/Playwright comparadas byte a byte com as DLLs dos nupkg públicos, evitando confundir packages antigos com a versão publicada.
- Round-trip completo do editor: sucesso, inclusive configuração, persistência, controles exclusivos, modo desligado, tooltip assistido por foco, homologação assistida, Recorder e worker com fixtures locais. Não foram utilizados serviços externos de CAPTCHA/provedores.

## Revisão independente e correção

A revisão final identificou um bloqueio concreto: selects restritos substituíam valores não reconhecidos pelo primeiro item, e faltavam canais Chrome/Edge beta/dev/canary. Isso foi corrigido antes do commit da entrega.

O editor agora oferece os **13** valores efetivamente suportados pelo runtime, preserva casing/whitespace reconhecidos de navegador, apresenta seleções desconhecidas sem convertê-las em defaults e rejeita valores inválidos ativos antes de salvar. Valores desconhecidos ou null inativos são preservados. Defaults para campos ausentes continuam true/bezier/legacy. O escopo da homologação assistida permanece nos três browsers previamente suportados.

Assertions reais adicionadas: chrome-beta sobrevive a uma edição não relacionada; algoritmo inválido e null inativos permanecem intactos; algoritmo/modo inválidos ativos produzem erro sem alterar o arquivo; null ativo é rejeitado; browser inválido não vira SpyBrowser automaticamente. O build e todo o EditorRoundTrip passaram novamente após a correção. O executor excedeu seu deadline depois de concluir a prova; somente sua árvore de processos foi encerrada. O pai verificou fonte/prova e repetiu diretamente build/round-trip com exit 0.

Também foi testado um clone público novo do commit `07021607f1270da857e6fbf390303b9da27b8109`, usando configurações Git vazias (sem credential helper) e outro cache NuGet novo em D: clone, bootstrap, restore completo, build completo e checks focados com três browsers/casos passaram. Essa prova cobre o código de produto da entrega; o follow-up seguinte altera apenas teste/documentação.

O primeiro CI da integração identificou uma assertion recém-adicionada que chamava o validador com algoritmo ativo inválido sem esperar a exceção correta. Foi corrigida para exigir a rejeição do algoritmo ativo, mantendo o teste separado de campo inativo e explicitando o browser SpyBrowser independentemente da lane Chromium. Nenhum validador foi relaxado. A suite completa `RpaFlow.PlaywrightChecks` passou localmente tanto no caminho SpyBrowser quanto no Chromium bruto, sem mudar deadlines.

Logs locais preservados em `artifacts/spybrowser-ui-integration/`, incluindo `beta21-final-*`, `select-final-*`, `full-playwright-*` e a prova de preservação. Não se afirma que os builds iniciais com pacote ef2 ou beta2 eram execuções desta versão pública. A licença do dataset é atestação do operador, não revisão jurídica independente.
