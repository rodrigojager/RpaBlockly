# ADR-017 — ZIP determinístico e seguro

Estado: Aceita

## Contexto

O ZIP é uma fronteira não confiável e pode explorar path traversal, duplicidade,
compressão extrema ou tipos especiais.

## Decisão

A extensão ordena entradas, serializa UTF-8 canônico e fixa metadados. O
importador inspeciona sem extrair, rejeita paths inseguros, duplicados sem
diferença de caixa, symlink, limites e razão de compressão, e valida SHA-256 e
tamanho antes de desserializar.

## Alternativas recusadas

- `ExtractToDirectory` direto: expõe Zip Slip e estado parcial.
- Confiar apenas na extensão: o arquivo pode ser adulterado depois.

## Consequências

Bundles equivalentes têm bytes reproduzíveis. Inspeção exige leitura limitada.

## Rollback

Apagar staging; nenhum arquivo é escrito no projeto antes do apply.

## Testes e evidências

Goldens, tamper por classe, Zip Slip, Zip Bomb, duplicidade e symlink.
