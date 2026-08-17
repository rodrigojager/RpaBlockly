# Harness Scrapling de referência

Esta ferramenta de desenvolvimento fixa o comportamento externo usado para calibrar a implementação C# da heurística. Ela não é referenciada pelo worker, pelo editor nem pelo runtime.

Referência auditada: Scrapling `v0.4.14`, commit `5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f`. O harness chama a implementação real de `Selector.__calculate_similarity_score`; os checks .NET comuns leem apenas os golden files versionados.

Use exclusivamente HTML sintético ou sanitizado, sem cookies, storage, tokens, credenciais, valores de inputs ou conteúdo de produção.

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.lock
.\.venv\Scripts\python generate_golden_files.py
```

As divergências de segurança do RpaBlockly estão registradas no ADR-008: rejeição de empate para uso singular, `runnerUpGap`, limites de varredura, descarte de atributos sensíveis, exigência de estado e aprendizado provisório.
