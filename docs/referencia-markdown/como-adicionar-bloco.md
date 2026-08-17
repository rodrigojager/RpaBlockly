# Como adicionar ou alterar um bloco V2

Um bloco só está completo quando contrato, runtime, editor, migração, documentação
e testes concordam.

## Sequência obrigatória

1. Adicione o tipo em `FlowActionCatalog`.
2. Atualize `schemas/flow-v2.schema.json` e, se necessário, locators/policy.
3. Atualize os DTOs e validadores em `src/RpaFlow.Contracts/V2`.
4. Gere TypeScript:

   ```powershell
   dotnet run --project tools/RpaFlow.ContractGenerator
   ```

5. Implemente o handler V2 e registre-o em `V2FlowActionHandlerRegistry`.
6. Toda localização de negócio deve passar por `LocatorResolver`.
7. Acrescente o bloco em `wwwroot/v2/action-catalog.js`; use papéis de locator e
   `FieldLocatorReference`, sem campo selector.
8. Atualize serialização/validação local somente quando a propriedade não for
   coberta pelo mecanismo genérico.
9. Se o tipo existia em V1, atualize o migrador e a família do baseline.
10. Atualize este catálogo e os exemplos relevantes.

## Testes mínimos

- fixture válida e inválida em ContractsChecks;
- validação cruzada em PackagesChecks quando houver locator/subflow/policy;
- execução do handler em página-fixture;
- round-trip e instanciação do bloco no editor;
- cobertura V1/V2 do migrador quando aplicável;
- ausência de acesso direto a `Page.Locator` fora do compilador.

Execute `tools/Run-Checks.ps1`. A suíte falha se os 32 tipos do catálogo e os
blocos/handlers deixarem de coincidir ou se os tipos TypeScript estiverem defasados.

Mudança incompatível não reutiliza uma versão de schema existente: crie nova
versão e documente a migração.
