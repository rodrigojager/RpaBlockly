# Corpus e benchmark de CAPTCHA

O manifesto `corpus-v1.json` expande deterministicamente 280 casos sintéticos:
200 imagens OCR, 50 áudios e 30 tarefas geométricas. Os assets são produzidos em
memória e não são versionados. Este corpus mede regressões reproduzíveis do
pipeline; não representa eficácia universal em desafios de fornecedores reais.

Execute a partir de `services/captcha-solver`:

```powershell
python -m benchmarks.run_corpus `
  --json ../../artifacts/captcha-benchmark/report.json `
  --csv ../../artifacts/captcha-benchmark/report.csv `
  --require image_ocr `
  --report-only
```

`--require` torna indisponibilidade da capacidade um erro. `--report-only` não
falha por threshold, mas mantém `qualityPassed=false` no relatório. Remova essa
flag em um gate de promoção depois que o hardware, os modelos e o corpus de
avaliação estiverem congelados.

Imagem usa Pillow e o ONNX configurado. Áudio exige `espeak-ng` e o cache offline
do Whisper fixado. Geometria exige o router LiteLLM explicitamente habilitado.
Capacidades ausentes geram linhas `skipped`, nunca sucesso implícito.

Os relatórios incluem apenas IDs, hashes SHA-256, identidade do solver/modelo,
latência e métricas. Não incluem bytes/base64, respostas reconhecidas, prompts,
segredos, paths absolutos ou cookies. Assets reais só podem entrar por um corpus
privado autorizado, sanitizado e fixado por hash; não colete desafios de sessões
de clientes para ampliar este conjunto.

Metas da revisão 1:

- OCR: correspondência exata mínima de 95% e CER máximo de 5%;
- áudio: correspondência exata mínima de 95% e WER máximo de 5%;
- geometria: ação/papel corretos em pelo menos 95%, com limites de erro por cohort.
