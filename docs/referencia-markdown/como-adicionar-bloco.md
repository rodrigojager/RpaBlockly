# Como adicionar ou alterar um bloco

Uma capacidade só está pronta quando o caminho inteiro permanece sincronizado:

```text
modelo JSON
  ↕
catálogo e validador
  ↕
handler C#
  ↕
bloco e toolbox
  ↕
JSON → Blockly
  ↕
Blockly → JSON
  ↕
validação do frontend e microservidor
  ↕
testes e documentação
```

## Antes de criar um tipo

Escolha a solução nesta ordem:

1. **Configurar um bloco existente:** quando a diferença for seletor, valor, timeout, comparação, destino ou iframe.
2. **Compor blocos:** quando ações pequenas em sequência, condição, loop ou subfluxo expressarem a etapa.
3. **Adicionar propriedade genérica:** quando a semântica existente precisar de uma opção reutilizável e compatível.
4. **Criar um novo tipo técnico:** quando houver capacidade nova, declarativa e útil em outros RPAs.
5. **Criar política ou adapter específico:** quando houver protocolo, segurança ou efeito que não caiba no contrato genérico.

Não crie um tipo para representar uma etapa de negócio como “preencher nota da empresa X”. Use um nome operacional na instância e tipos técnicos reutilizáveis.

## Perguntas obrigatórias

Antes de implementar:

- Qual é a entrada?
- Qual é o efeito observável?
- Qual é a saída e onde será gravada?
- A ação pode ser repetida com segurança?
- Pode produzir efeito remoto ou irreversível?
- Precisa armar evento antes da interação?
- Exige exatamente um alvo?
- Precisa de timeout?
- Funciona em condição, loop e subfluxo?
- Requer segredo ou capability?
- Quais limites impedem abuso ou execução infinita?
- Um bloco existente mais subfluxo já resolve?

## Adicionar um `action.type`

### 1. Contrato

Em `src/RpaFlow.Contracts/Flow/`:

1. adicione propriedades necessárias a `FlowActionDefinition`, reutilizando campos comuns quando a semântica for a mesma;
2. inclua o tipo em `FlowActionCatalog` com suas capabilities;
3. atualize `FlowDefinitionValidator` com obrigatoriedade, exclusividade, enumerações, ranges e restrições de aninhamento;
4. mantenha `type` técnico, reutilizável, em inglês e `camelCase`.

Se uma propriedade desconhecida precisar ser recusada, lembre que a desserialização estrita reconhece o modelo inteiro. O validador deve rejeitar combinações conhecidas porém semanticamente inválidas quando isso for necessário.

### 2. Interpretação

Em `src/RpaFlow.Playwright/Flow/`:

1. escolha o handler da categoria correta ou crie um handler pequeno;
2. declare o tipo em `SupportedTypes`;
3. implemente entrada, efeito, saída e falhas documentados;
4. respeite `CancellationToken`;
5. use `FlowDataContext`, localizador e destinos comuns;
6. não registre valores potencialmente sensíveis;
7. confirme que `FlowActionHandlerRegistry` continua sincronizado com o catálogo.

`JsonFlowActionStep` já consome orçamento e publica eventos. Ações compostas devem chamar `ExecuteNestedActionsAsync` para manter essas garantias nas ações internas.

### 3. Blockly

Em `src/RpaFlow.Editor/wwwroot/app.js`:

1. defina o bloco `rpa_*` com campos, tooltip e conexões corretas;
2. adicione-o à categoria adequada da toolbox;
3. atualize `actionToBlockType` ou a seleção especial quando vários blocos compartilharem o mesmo tipo;
4. implemente JSON → bloco em `createBlock` e helpers;
5. implemente bloco → JSON em `blockToAction`;
6. inclua as validações do frontend;
7. preserve IDs, valores tipados, defaults e ações aninhadas;
8. trate renomeação de variáveis quando a propriedade aceitar source.

Toda propriedade emitida no JSON precisa voltar ao mesmo campo quando o documento for reimportado.

### 4. Microservidor

O servidor usa `RpaFlow.Contracts` em `FlowDocumentValidator`. Confirme que:

- documentos válidos do frontend são aceitos;
- documentos inválidos não são gravados;
- UTF-8, temporário e `.bak` continuam preservados;
- o frontend e o servidor apresentam regras equivalentes.

### 5. Testes

Adicione ou amplie fixtures para:

- documento válido mínimo;
- campos obrigatórios ausentes;
- literais e sources;
- enumerações e ranges;
- propriedades opcionais presentes e ausentes;
- ação dentro de `if`, `repeat`, `forEach` e subfluxo, quando permitida;
- round-trip JSON → Blockly → JSON;
- execução do handler em ambiente local ou simulado;
- falha, cancelamento, orçamento e saída `runtime.*`;
- compatibilidade com o template, o exemplo e os fluxos atingidos.

Execute:

```powershell
.\tools\Validar-Base.ps1
```

Se o bloco alterar um fluxo específico, execute também `--validate-only` naquele RPA.

### 6. Documentação

Atualize:

- [Catálogo de blocos](catalogo-de-blocos.md);
- [Schema versão 1](flow-schema-v1.md), quando houver regra de contrato;
- [Guia do editor](guia-editor-blockly.md), quando houver mudança de UI;
- README e Draw.io do RPA, quando sua sequência mudar;
- template, quando a capacidade fizer parte do caminho inicial recomendado.

## Adicionar uma propriedade

Defina antes de codificar:

| Decisão | Pergunta |
| --- | --- |
| Nome e tipo | Como aparece no JSON? |
| Presença | Obrigatória, opcional ou default? |
| Origem | Literal, `*Source` ou ambos de forma exclusiva? |
| Compatibilidade | O que acontece quando JSON antigo omite a propriedade? |
| Interface | Qual campo edita o valor? |
| Importação | Como um valor ausente ou desconhecido aparece no bloco? |
| Execução | Qual handler consome a propriedade? |
| Validação | Quais layers recusam valor inválido? |

Para campos comuns como localizador, timeout, valor ou destino de artefato, use os helpers existentes. Não crie nomes diferentes para a mesma semântica.

Se a propriedade só existir no modelo/handler e for perdida pelo Blockly, ela ainda não está pronta para produção.

## Compatibilidade e versionamento

O único contrato atual é `schemaVersion: 1`. Adicionar campo opcional com default compatível não exige versão nova.

Considere `schemaVersion: 2` somente quando:

- a semântica de uma propriedade existente mudar de forma incompatível;
- uma estrutura antes válida precisar se tornar inválida;
- o runtime não conseguir inferir com segurança qual interpretação aplicar.

Uma migração futura precisa desserializar o documento antigo, transformar, validar, mostrar no Blockly, salvar com backup e comprovar equivalência.

## Ações perigosas

Para qualquer ação com efeito remoto:

- documente se é idempotente;
- não use `force` para vencer intertravamentos;
- arme download, popup ou nova página antes da interação que dispara o evento;
- pare antes de confirmar, enviar, cadastrar ou excluir sem autorização;
- exija policy/adapter específico quando a proteção depender do sistema;
- classifique falhas e retry sem registrar dados sensíveis.

`safeFinalConfirmation` é o exemplo de capacidade deliberadamente restrita: uma única instância, terminal e dependente de política explícita.

## Definição de pronto

Uma alteração está concluída quando:

- editor e runtime aceitam e rejeitam as mesmas estruturas relevantes;
- o JSON salvo é executado sem o Blockly;
- o round-trip preserva a semântica;
- todos os tipos continuam sincronizados entre catálogo e handlers;
- JSONs existentes mantêm seus defaults;
- o novo bloco não contém regra de portal quando ela poderia ser configuração;
- efeitos perigosos continuam fora do trecho não autorizado;
- documentação e testes representam o comportamento entregue.
