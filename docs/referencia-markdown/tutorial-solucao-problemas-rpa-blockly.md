# Tutorial de solução de problemas do template RPA Blockly

Este guia registra problemas encontrados durante a evolução do runner .NET, do fluxo JSON schema 1 e do editor Blockly compartilhado. As soluções são genéricas e devem ser tentadas antes de criar código específico para um RPA.

Para funcionamento normal e referência de contrato, consulte primeiro o [índice da documentação](../README.md), o [guia do editor](guia-editor-blockly.md), o [schema versão 1](flow-schema-v1.md) e o [catálogo de blocos](catalogo-de-blocos.md). Este arquivo é voltado a diagnóstico.

## Ordem recomendada de diagnóstico

1. Validar configuração e JSON sem abrir o navegador.
2. Confirmar que o editor importa e exporta o fluxo sem perda semântica.
3. Executar o navegador visível.
4. Inspecionar seletor, unicidade, visibilidade e estado do elemento.
5. Confirmar o comportamento do JavaScript da página após a interação.
6. Verificar artefatos e `runtime.*`.
7. Parar antes de qualquer envio, cadastro, exclusão ou confirmação não autorizada.

Comandos básicos, executados em sequência:

```powershell
dotnet build RpaBlockly.slnx
dotnet run --project rpas/RpaNome/RpaNome.csproj --no-build -- --validate-only
node --check src/RpaFlow.Editor/wwwroot/app.js
```

Não execute `dotnet build` e `dotnet run` ao mesmo tempo sobre os mesmos projetos. Ambos escrevem em `obj` e podem disputar a DLL intermediária.

## 1. Build falha porque uma DLL está sendo usada

### Sintoma

O compilador informa que não conseguiu abrir uma DLL em `obj/Debug` para escrita porque outro processo está usando o arquivo.

### Causa observada

Build e execução de validação foram iniciados em paralelo. Os dois processos tentaram produzir o mesmo artefato intermediário.

### Solução

Execute as operações .NET sequencialmente:

```powershell
dotnet build RpaBlockly.slnx
dotnet run --project rpas/RpaNome/RpaNome.csproj --no-build -- --validate-only
```

Verificações que não compartilham `bin` ou `obj`, como `node --check`, podem continuar em paralelo.

## 2. O editor não deve rodar por `file://`

### Sintoma

O Blockly abre como arquivo local, mas não consegue carregar ou salvar configuração e fluxo de forma confiável.

### Causa

Uma página `file://` não possui um backend autorizado a persistir os arquivos do projeto. Também impõe restrições diferentes de origem e recursos locais.

### Solução

Use o microservidor ASP.NET Core compartilhado:

```powershell
.\abrir-editor.cmd rpas\RpaNome
```

Ou execute diretamente:

```powershell
dotnet run --project src/RpaFlow.Editor/RpaFlow.Editor.csproj -- --project-root C:\caminho\do\rpa
```

O Blockly fica armazenado localmente em `src/RpaFlow.Editor/wwwroot/vendor/blockly`; não use CDN.

## 3. O editor abre o RPA ou a configuração errada

### Sintoma

O título, as variáveis ou o fluxo pertencem a outro projeto.

### Diagnóstico

Confira:

- o argumento `--project-root`;
- `rpa.editor.json` dentro da pasta selecionada;
- `projectFile`, `configurationFile` e `flowFile` do perfil;
- a indicação `Conectado: ...` mostrada pela interface.

### Solução

Cada atalho deve apenas encaminhar a pasta correta ao inicializador compartilhado:

```cmd
@echo off
call "%~dp0abrir-editor.cmd" rpas\RpaNome
```

Não copie o frontend para dentro de cada RPA.

## 4. O editor continua mostrando uma versão antiga

### Sintoma

O JSON ou o perfil foi alterado, mas os blocos e campos continuam iguais.

### Causas comuns

- O microservidor ainda executa o binário anterior.
- A aba continua com recursos em cache.
- Outro processo ocupa a porta esperada.

### Solução

1. Descubra qual processo realmente escuta a porta.
2. Encerre somente o processo confirmado como `RpaFlow.Editor`.
3. Reinicie o microservidor apontando para a pasta correta.
4. Recarregue a aba.

Não encerre processos por nome ou porta sem antes verificar o alvo exato.

## 5. JSON e Blockly perdem propriedades no caminho de volta

### Sintoma

O runtime aceita o JSON, mas depois de importar e salvar no Blockly alguma propriedade desaparece ou muda.

### Causa

A propriedade foi implementada somente no modelo/runtime ou somente na interface. Também podem existir diferenças apenas de representação, como `optional: false` explícito e ordem de chaves.

### Solução

Toda capacidade precisa existir nos três lados:

1. Modelo e validação do schema.
2. Bloco, `actionToBlock` e `blockToAction`.
3. Handler do runtime.

No teste de round-trip, normalize somente valores padrão comprovadamente equivalentes e ordene as chaves antes da comparação. Nunca normalize uma diferença de comportamento.

Critério obrigatório:

```text
JSON de produção → Blockly → JSON de produção equivalente
```

## 6. Seletor funciona hoje, mas é longo ou frágil

### Sintoma

O seletor foi copiado do HTML inteiro, contém muitos níveis, classes de layout, índices ou atributos gerados pelo framework.

### Solução

Audite na seguinte ordem:

1. ID estável e único.
2. `name`, `data-*` funcional, papel ou nome acessível.
3. Classe funcional combinada com tipo e atributo semântico.
4. `frameSelectors` quando o elemento estiver em outro documento.
5. `scope` estável do formulário ou modal.
6. `hasText` quando o texto fizer parte do contrato visual.

Quando o texto variar por caso, use `hasTextSource`. Para localizar uma ação dentro da linha correta de tabela, use `scope` para a linha, `scopeHasTextSource` para o identificador do caso e `selector` para o controle interno. Não interpole dados do caso dentro de CSS.

Rejeite IDs efêmeros como `el-id-*` e atributos de compilação Vue como `data-v-*`. Confirme que ações singulares encontram exatamente um elemento. Não esconda ambiguidade com `.First` ou `.Nth`.

## 7. O campo mostra texto, mas a página ainda diz que é obrigatório

### Sintoma

`input.value` contém o texto esperado, porém o framework mantém erro, limpa o campo depois ou não habilita a continuação.

### Causa observada

As teclas foram enviadas rápido demais ou por uma estratégia equivalente a colagem. O DOM mudou, mas o modelo reativo da página perdeu parte dos eventos. Em um caso, o contêiner chegou a manter `is-success` enquanto o input estava vazio.

### Solução

Use `typeSequentially` quando a inspeção demonstrar dependência de eventos reais de teclado:

- `clearFirst: true` para limpar com teclado;
- `delayMs` suficiente para o componente, começando entre 50 e 100 ms;
- `blurAfter: true` para sair com `Tab`;
- espera posterior pelo sinal real de validação;
- conferência de que o valor permaneceu no input.

Não aceite somente a classe do contêiner. Combine valor persistido, ausência de mensagem de erro e sinal de sucesso do componente.

## 8. O site impede copiar ou colar

### Sintoma

A página exibe alertas de conteúdo não copiável, ignora o valor ou limpa o input.

### Solução

Não use clipboard nem preenchimento em bloco nesse componente. Digite caractere a caractere com `typeSequentially`. Valide o resultado após perder o foco.

Não generalize essa solução para todos os campos: `fill` continua mais simples e adequado quando o componente aceita preenchimento programático normal.

## 9. Upload limpa campos que já estavam preenchidos

### Sintoma

Os campos estavam corretos, mas ficam vazios ou inconsistentes depois de anexar um arquivo.

### Causa observada

O componente de upload atualizou ou recriou parte do formulário. Referências anteriores ficaram obsoletas e valores locais foram descartados.

### Solução

1. Anexe o arquivo primeiro quando o upload puder recalcular ou recriar o formulário.
2. Aguarde o nome do arquivo aparecer ou outro sinal real de conclusão.
3. Relocalize os inputs.
4. Preencha os dados do caso.
5. Valide novamente depois do `blur`.

Não use pausa fixa. Aguarde um elemento, estado de loading, atividade de rede ou estabilidade observável.

## 10. Campos pertencem ao JavaScript da página

### Sintoma

Um valor deveria ser calculado após upload ou AJAX, mas o RPA começa a preenchê-lo e mascara o comportamento do portal.

### Solução

Primeiro aguarde loading e estabilidade. Depois:

- preserve quando o valor já estiver correto;
- preencha somente se o campo estiver vazio e o RPA for autorizado a fornecer o valor;
- falhe quando o portal produzir valor divergente.

Use `preserveOrFill` para essa semântica. Não dispare `input`, `change` ou `blur` artificialmente como primeira tentativa.

## 11. O upload local funciona no Playwright, mas não no Chrome assistido

### Sintoma

O input de arquivo é encontrado, mas a ferramenta assistida não consegue anexar um caminho local.

### Causa possível

A extensão de controle do Chrome não recebeu permissão para acessar URLs de arquivo. Isso é uma restrição da ferramenta de inspeção assistida, não do `SetInputFilesAsync` usado pelo runtime .NET.

### Solução

Conceda à extensão a permissão de acesso a URLs de arquivo somente no ambiente de desenvolvimento e repita o teste. Em produção, valide o caminho antes de abrir o navegador e use o bloco `upload`.

## 12. MFA reaparece em uma nova instância

### Sintoma

O navegador do usuário está confiável, mas um novo contexto Playwright solicita 2FA.

### Causa

Chrome do usuário e Playwright não compartilham automaticamente a mesma sessão. Cada contexto novo começa isolado, salvo quando recebe um storage state válido.

### Solução

1. Execute uma instância assistida e autorizada para concluir o MFA.
2. Marque o dispositivo confiável quando a política permitir.
3. Salve a sessão por `Runtime.StorageStatePath` com `Runtime.SaveStorageState: true`.
4. Nas instâncias paralelas, use o mesmo arquivo somente para leitura e mantenha `SaveStorageState: false`.
5. Renove o arquivo por uma única execução assistida quando expirar.

O storage state é sensível, fica fora do Git e não contorna o MFA. Não tente obter OTP ou credenciais por canais não autorizados.

## 13. Execuções paralelas misturam dados

### Sintoma

Um caso recebe valores, anexos ou resultados de outro item do lote.

### Causa

Estado global, página, objeto JSON ou variável do item atual foi compartilhado entre execuções.

### Solução

O worker reserva o caso e cria um `FlowExecutionRequest` novo. O runtime faz cópia profunda de `Input`, `Configuration` e `Attachments`, cria `runtime` isolado e devolve `FlowExecutionResult` associado a `ExecutionId`, `WorkItemId` e `BatchId`.

O Blockly nunca escolhe o próximo item nem executa claim no banco.

## 14. Screenshot ou download precisa ir para pasta local ou UNC

### Sintoma

O arquivo colide, fica parcial ou o banco recebe um caminho antes de a gravação terminar.

### Solução

Configure destino, separação por execução e estratégia de conflito no próprio bloco. Para compartilhamento UNC:

- use caminho totalmente qualificado;
- autentique pela identidade do processo ou serviço, nunca pelo JSON;
- grave temporário na mesma pasta e publique o nome final ao concluir;
- escreva o caminho em `runtime.*` somente depois do sucesso;
- deixe o worker ou ação autorizada persistir esse resultado no banco.

## 15. A ação final não deve ser testada ainda

### Sintoma

O botão está habilitado e parece ser o último passo, mas o teste não possui autorização para enviar ou cadastrar.

### Solução

Pare antes do clique. Uma espera pelo botão habilitado e uma screenshot são suficientes para comprovar prontidão. Não inclua um clique comum no fluxo.

Quando houver autorização para testar apenas o diálogo, use uma proteção final específica que bloqueie o efeito, valide o alerta, capture evidência e cancele. Se o usuário proibir até a abertura do alerta, não use essa proteção.

## 16. O elemento existe, mas está dentro de um ou mais iframes

### Sintoma

O seletor é correto no DevTools, porém `page.Locator(selector)` encontra zero elementos. Em portais SAP, uma tela também pode abrir um popup em outro iframe de topo, fora da cadeia usada pelo conteúdo anterior.

### Solução

1. Identifique cada iframe desde a página principal até o documento que contém o elemento.
2. Preencha `frameSelectors` na ordem externo → interno, usando identidades auditadas, por exemplo `["#contentAreaFrame", "#isolatedWorkArea"]`.
3. Use `scope` somente depois da entrada no último iframe, para limitar a busca dentro daquele documento.
4. Quando um popup usar outro iframe de topo, inicie uma nova lista, como `["#URLSPW-0"]`; não continue a cadeia anterior.
5. Confirme a visibilidade do iframe e do elemento antes da ação.

Evite um seletor amplo como `iframe` quando o documento também cria frames auxiliares. Em caso de timeout de uma espera obrigatória, o runtime registra o mapa de frames, a relação pai-filho, os atributos do iframe proprietário e a quantidade de alvos encontrados. Parâmetros `jsessionid` são ocultados nesse diagnóstico. Use o mapa para escolher uma identidade estável; não use posição, `first`, `nth` ou sufixos temporários.

Não transforme nomes ou IDs específicos de um portal em código C#. A fábrica genérica de localizadores deve encadear `FrameLocator` conforme o JSON, e a interface precisa preservar a lista no round-trip.

## 17. Um erro conhecido aparece durante uma espera longa

### Sintoma

Depois de uma ação remota, o portal substitui o conteúdo por uma página de erro conhecida, mas o RPA continua aguardando por vários minutos o seletor de sucesso até expirar.

### Solução

Use blocos existentes para modelar uma corrida declarativa:

1. No primeiro `wait` depois da ação, use uma união CSS com o seletor de sucesso e o seletor estável da página de erro.
2. Não limite essa espera ao `scope` da linha quando a página de erro substitui o documento inteiro.
3. Logo depois, use `if` com condição `element` para identificar especificamente o erro.
4. No ramo verdadeiro, use `fail` com uma mensagem operacional que explique o estado, a necessidade de conferência manual e a política de reenvio.
5. No caminho normal, mantenha uma confirmação escopada ao caso antes de registrar sucesso.

Não use apenas um `if` imediatamente após o clique: a resposta pode chegar de forma assíncrona depois da avaliação. Também não use identificadores dinâmicos, como números de referência gerados pela infraestrutura. Para efeitos irreversíveis, preserve o checkpoint antes do clique; assim, uma falha detectada depois dele continua sem reenvio automático.

## 18. UTF-8 e mojibake

### Sintoma

Textos aparecem como sequências corrompidas, seletores textuais falham ou caracteres portugueses viram entidades.

### Solução

- Leia bytes com `UTF8Encoding(false, true)`.
- Salve UTF-8 sem substituir `á`, `é`, `ó`, `ã`, `õ` ou `ç` por entidades HTML.
- Valide os bytes finais com decoder estrito.
- Procure o caractere de substituição Unicode e sequências conhecidas de mojibake.
- Reabra JSON, Markdown e Draw.io após a gravação.

## Checklist antes de considerar uma correção concluída

- O problema foi reproduzido e a causa observada.
- A solução usa um bloco existente ou amplia o catálogo de forma genérica.
- Modelo, validador, Blockly, conversão e handler continuam sincronizados.
- O fluxo passa em `--validate-only`.
- JSON → Blockly → JSON preserva a semântica.
- Seletores singulares são únicos e estáveis.
- Valores permanecem corretos depois de loading, upload e perda de foco.
- Nenhum segredo foi inserido no fluxo.
- Artefatos só são publicados após sucesso.
- Nenhuma ação irreversível não autorizada foi executada.
- Todos os arquivos editados continuam em UTF-8 válido.
