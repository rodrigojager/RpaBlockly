# Schemas do pacote V2

Os contratos normativos são:

- `schemas/flow-v2.schema.json`;
- `schemas/locators-v1.schema.json`;
- `schemas/rpa-policy-v1.schema.json`.

Consulte [Pacote operacional V2](../v2/pacote-operacional.md) para exemplos e
semântica. Os tipos TypeScript em `schemas/generated/contracts.ts` são gerados por
`tools/RpaFlow.ContractGenerator`; não os edite manualmente.

Para validar C# e TypeScript contra os mesmos goldens:

```powershell
dotnet run --project tests/RpaFlow.ContractsChecks
npm ci --prefix tools/schema-conformance
npm run check --prefix tools/schema-conformance
```

Regras entre documentos — referências órfãs, cardinalidade por ação, ciclos,
policy e fingerprints — são aplicadas por `RpaPackageValidator` depois da validação
estrutural de cada schema.
