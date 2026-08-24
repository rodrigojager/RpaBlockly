# Gate REC-G12 — evidências do release candidate

| Tarefa | Evidência versionada ou automatizada |
|---|---|
| REC-130 | `tests/fixtures/recorder-site` contém formulário, SPA, iframe, popup, select, upload, navegação e DOM mutável. |
| REC-131 | `RpaFlow.EditorRoundTrip` carrega manifesto, service worker e side panel MV3 no Chromium; em seguida usa o content script compilado, eventos confiáveis e o exportador de produção. O consentimento do diálogo nativo permanece no aceite REC-140. |
| REC-132 | O E2E executa inspect, review, mappings, validate e apply no editor real. |
| REC-133 | O snapshot publicado é carregado pelo `FileRpaPackageStore`, entregue ao `WorkItemProcessor` e executado em `strict` pelo runtime. |
| REC-134 | A fixture remove o `data-testid` primário; `fallback` seleciona alternativa exata e conclui. |
| REC-135 | Checks de packages e worker confirmam snapshot imutável, CAS, execuções independentes e descarte de aprendizado quando o resultado final é `Validated`. |
| REC-136 | Testes TypeScript cobrem determinismo, suspensão, acessibilidade e orçamento de tempo/memória. |
| REC-137 | Threat model, `THIRD_PARTY_NOTICES.md`, SBOM SPDX e auditorias NuGet/npm fazem parte do gate. |
| REC-138 | `release.mjs` faz dois builds byte a byte e verifica o checksum versionado. |
| REC-139 | Manuais de cliente, desenvolvedor, privacidade e troubleshooting estão em `docs/recorder`. |
| REC-140 | Launcher loopback, área descartável, controles sem edição de JSON, verificador de privacidade, roteiro fechado e relatório pendente estão versionados. A promoção para GA ainda exige execução e sign-off por pessoa independente. |

Comandos bloqueantes:

```powershell
.\tools\Run-Checks.ps1
.\tools\Test-Dependencies.ps1
.\tools\Generate-Sbom.ps1
```

O gate automatizado não substitui o sign-off humano descrito em
`teste-instalacao-limpa.md`. O estado explícito está em
`relatorio-instalacao-limpa.md`; o release permanece identificado como RC até
essa evidência externa ser preenchida e aprovada.
