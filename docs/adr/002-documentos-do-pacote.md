# ADR-002 — Documentos do pacote

## Estado

Aceita.

## Contexto

Fluxo, receita de localização e política de resiliência mudam por motivos e em
ritmos distintos. Embutir tudo nas ações duplicaria seletores e impediria revisar
ou promover um candidato sem reescrever o roteiro.

## Decisão

Um pacote operacional contém `flow.production.json`,
`locators.production.json` e `rpa.policy.json`. O fluxo guarda lógica e
referências; receitas e fingerprints ficam no catálogo; comportamento de
resolução e aprendizado fica na política. Os três documentos formam uma única
revisão validada e publicada atomicamente.

Na V2, `target` significa exclusivamente o uso do localizador principal. Destinos
`runtime.*` antes chamados `target` passam a se chamar `output`.

## Alternativas recusadas

- um JSON monolítico: acopla autoria, resolução e operação;
- seletor copiado em cada bloco: cria divergência e dificulta promoção;
- política por ação: aumenta o contrato e torna o comportamento imprevisível.

## Consequências

Não se publica documento parcial. Uma alteração de locator cria nova revisão do
pacote, mesmo sem mudança do fluxo, e toda referência pode ser validada antes da
execução.

## Rollback

Mover o ponteiro atual para uma revisão anterior completa; nunca recombinar
documentos de revisões diferentes.

## Testes e evidências

`RpaFlow.PackagesChecks` recusa referência ausente, cardinalidade incompatível e
conjunto incoerente, além de provar publicação atômica em memória e arquivo.
