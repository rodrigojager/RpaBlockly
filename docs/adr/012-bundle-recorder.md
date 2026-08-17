# ADR-012 — Bundle Recorder nativo V2

Estado: Aceita

## Contexto

O Recorder precisa transferir autoria, evidências e pendências sem criar um
quarto formato operacional ou exigir conversão no editor.

## Decisão

O ZIP `.rpablockly.zip` contém em `package/` exatamente os três documentos V2.
Metadados de gravação ficam em diretórios adjuntos versionados. O ZIP nunca
contém mecanismo de replay.

## Alternativas recusadas

- Schema intermediário: duplicaria contratos e mapeamentos.
- Script executável gravado: ampliaria a superfície de ataque.
- Serviço remoto obrigatório: impediria operação local e offline.

## Consequências

O editor pode validar o pacote com os contratos oficiais. O envelope possui
versão própria e pode evoluir sem alterar o runtime.

## Rollback

Desabilitar a importação/geração do envelope preserva integralmente a V2.

## Testes e evidências

Schemas cruzados, bundles golden, integridade, importação read-only e E2E.
