# ADR-013 — Chrome mínimo

Estado: Aceita

## Contexto

O side panel e sua abertura programática precisam de uma base previsível.

## Decisão

O Recorder exige Chrome 116 ou superior e declara `minimum_chrome_version` no
Manifest V3. A extensão não implementa popup alternativo para versões antigas.

## Alternativas recusadas

- Chrome 114 com degradação: perde abertura programática consistente.
- UI somente em popup: prejudica timeline e revisão contínua.

## Consequências

Instalações antigas recebem erro explícito. A interface pode usar side panel.

## Rollback

Remover a extensão; o editor e a V2 não dependem do Chrome.

## Testes e evidências

Validação estática do manifest e instalação limpa no Chrome suportado.
