# ADR-003 — Fonte canônica

## Estado

Aceita.

## Contexto

O contrato é consumido por C#, editor JavaScript/TypeScript e, posteriormente,
pelo Recorder. Definições independentes tenderiam a discordar sobre nomes,
enums, campos obrigatórios e rejeição de propriedades extras.

## Decisão

JSON Schemas Draft 2020-12 versionados em `schemas/` são a descrição portável do
contrato. DTOs C# e tipos TypeScript são verificados contra os mesmos golden
files. Regras entre documentos ou dependentes do tipo de ação permanecem em
validadores semânticos explícitos.

## Alternativas recusadas

- DTO C# como única fonte: não é diretamente verificável no navegador;
- interfaces TypeScript manuscritas: permitem drift silencioso;
- aceitar JSON e validar somente durante a execução: posterga erro de autoria.

## Consequências

Mudança incompatível exige nova versão de schema. O gerador TypeScript registra o
hash dos schemas e o CI falha se o arquivo gerado ou os resultados divergirem.

## Rollback

Restaurar em conjunto schemas, tipos gerados e DTOs da revisão anterior. Não é
permitido restaurar somente um consumidor.

## Testes e evidências

`RpaFlow.ContractsChecks` usa os DTOs e validadores C# estritos;
`tools/schema-conformance` usa Ajv
2020 e o compilador TypeScript. Ambos classificam o mesmo conjunto de fixtures
válidas e inválidas, incluindo propriedades desconhecidas e formatos.
