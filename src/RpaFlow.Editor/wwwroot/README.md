# Frontend Blockly V2

Esta pasta contém a interface visual e a cópia local do Blockly 13.1.1, sem
CDN. O frontend abre uma revisão de pacote recebida do microservidor e mantém
os três documentos operacionais no estado da sessão.

Os módulos em `v2/` separam boot, estado, API, catálogo de ações, toolbox,
serialização, validação, campo de referência e drawers de locators/policy. Os 35
blocos representam os 32 tipos de ação; blocos web persistem somente
`locatorId` e cardinalidade, nunca uma receita ou seletor bruto.

O workspace exportado preserva a disposição visual. A execução usa apenas o
pacote publicado pelo backend. Salvar exige a revisão esperada e publica fluxo,
locators e policy atomicamente; conflitos precisam ser recarregados, comparados
e confirmados de forma explícita.

Valores administrados pelo painel ficam em `Blockly.Variables`; resultados de
execução ficam em `runtime.*`. Segredos não pertencem ao pacote, ao workspace ou
às evidências.

Consulte o [guia do editor](../../../docs/referencia-markdown/guia-editor-blockly.md),
o [catálogo](../../../docs/referencia-markdown/catalogo-de-blocos.md) e os avisos
em `THIRD_PARTY_NOTICES.md` e `vendor/blockly/LICENSE`.
