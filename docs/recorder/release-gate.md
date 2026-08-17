# Gate REC-G12 — evidências do release candidate

| Tarefa | Evidência versionada ou automatizada |
|---|---|
| REC-130 | `tests/fixtures/recorder-site` contém formulário, SPA, iframe, popup, select, upload, navegação e DOM mutável. |
| REC-131 | `RpaFlow.EditorRoundTrip` injeta o content script compilado, usa eventos confiáveis do Playwright e exporta pelo gerador de produção. |
| REC-132 | O E2E executa inspect, review, mappings, validate e apply no editor real. |
| REC-133 | O snapshot publicado é carregado pelo `FileRpaPackageStore` e executado em `strict`. |
| REC-134 | A fixture remove o `data-testid` primário; `fallback` seleciona alternativa exata e conclui. |
| REC-135 | Checks de packages e worker confirmam snapshot imutável, CAS e execuções independentes. |
| REC-136 | Testes TypeScript cobrem determinismo, suspensão, acessibilidade e orçamento de tempo/memória. |
| REC-137 | Threat model, `THIRD_PARTY_NOTICES.md`, SBOM SPDX e auditorias NuGet/npm fazem parte do gate. |
| REC-138 | `release.mjs` faz dois builds byte a byte e verifica o checksum versionado. |
| REC-139 | Manuais de cliente, desenvolvedor, privacidade e troubleshooting estão em `docs/recorder`. |
| REC-140 | O roteiro de instalação limpa está versionado para sign-off por pessoa independente antes da promoção de RC para GA. |

Comandos bloqueantes:

```powershell
.\tools\Run-Checks.ps1
.\tools\Test-Dependencies.ps1
.\tools\Generate-Sbom.ps1
```

O gate automatizado não substitui o sign-off humano descrito em
`teste-instalacao-limpa.md`. O release permanece identificado como RC até essa
evidência externa ser registrada.
