# ADR-008 — Heurística nativa

## Estado

Aceita.

## Decisão

A produção usa implementação C# determinística. Scrapling `v0.4.14`, commit `5d213a2`, serve somente como referência executável para fixtures e calibração.

O score-base preserva os fatores observados em `Selector.__calculate_similarity_score`: tag, texto, chaves e valores de atributos, `class`, `id`, `href`, `src`, caminho, pai e irmãos. `ScraplingCompatibleSequenceMatcher` porta o algoritmo Ratcliff/Obershelp usado por `difflib.SequenceMatcher` para os tamanhos cobertos pelas fixtures.

O `RpaSafetyAdjustedScorer` é uma camada separada. Ela acrescenta `role`, nome acessível e penalizações de visibilidade e habilitação sem atribuir esses ajustes ao Scrapling.

## Divergências intencionais

- o RpaBlockly rejeita empate para usos singulares; o Scrapling pode devolver todos os vencedores;
- além da confiança mínima, o RpaBlockly exige distância mínima para o segundo colocado;
- atributos são ordenados antes da comparação, pois o JSON canônico não usa a ordem de atributos HTML como informação;
- o texto vem de uma coleta DOM limitada e sanitizada; valores de inputs, senhas e marcas privadas nunca entram no fingerprint;
- a varredura tem limites explícitos de nós, texto, atributos, ancestrais, irmãos, tempo e memória;
- `many`, `hidden` e `detached` não usam aprendizado singular;
- frames e scope precisam ser resolvidos por receitas exatas antes da heurística;
- candidatos exatos e fallbacks manuais são tentados antes da heurística;
- um resultado heurístico é provisório até a execução terminar com `Succeeded`.

## Alternativas recusadas

- Python ou Scrapling no worker: acrescentaria runtime, IPC e outro ponto de falha operacional;
- Levenshtein ou Jaro-Winkler: mudariam silenciosamente a distribuição dos scores;
- aceitar somente threshold: permitiria escolher entre candidatos praticamente empatados;
- persistir imediatamente: uma ação posterior poderia falhar depois de promover um alvo incorreto.

## Consequências

Python não é dependência do runtime. Atribuição BSD-3-Clause e divergências intencionais são documentadas.

## Rollback

Definir a política como `strict` ou `fallback` desliga integralmente a coleta heurística sem alterar fluxo ou catálogo. Reverter o componente adaptativo não muda os schemas dos candidatos exatos.

## Evidência

`tools/scrapling-reference` executa o commit fixado e gera golden files sanitizados. Os checks C# validam metadados, ranking, vencedor, determinismo, baixa confiança e rejeição de empate. A suíte comum não instala Python.
