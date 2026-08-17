# Guia do editor Blockly V2

## Abrir

Cada RPA possui `rpa.editor.json` com `rpaId`, `packageStoreRoot`, projeto e
configuração. Abra pela raiz:

```powershell
.\abrir-editor.cmd examples\RpaExemplo
```

O servidor escuta apenas loopback, gera um token por sessão e não permite API de
pacote sem o header local. Arquivos estáticos não dependem de CDN.

## Sessão de edição

O editor abre uma revisão completa. A tela possui:

- Blockly com 35 blocos;
- JSON gerado do fluxo;
- catálogo pesquisável por locator ID ou nome amigável;
- drawer de locator com candidatos, receitas, fingerprints, origem e ordem;
- drawer de `rpa.policy.json`;
- propriedades avançadas da ação;
- warnings de locator ausente, não utilizado e cardinalidade;
- identidade da revisão e painel de conflito.

`FieldLocatorReference` grava somente `locatorId` e cardinalidade no bloco. Receitas
nunca são copiadas para o workspace ou para a ação.

## Salvar

O botão de salvar envia flow, locators, policy e `expectedRevision`. O backend
desserializa em UTF-8 estrito, valida o conjunto e publica uma nova revisão pelo
file store. As APIs `/api/flow`, `/api/locators` e `/api/policy` também substituem
um componente, mas sempre recarregam e publicam o pacote completo.

Se outra sessão publicou antes, o backend responde conflito. Escolha recarregar,
comparar ou salvar uma nova revisão depois de revisão explícita. Não existe
sobrescrita silenciosa.

## Fluxo recomendado

1. abra o pacote e anote revisão/policy;
2. crie ou edite locators no catálogo;
3. escolha locator IDs nos blocos;
4. corrija erros e revise warnings;
5. aplique alterações da policy ao rascunho;
6. salve o pacote;
7. reabra e confira a nova revisão;
8. execute o host com `--validate-only`.

## Configuração

O diálogo de configuração edita o JSON indicado por `configurationFile` no perfil.
Segredos podem existir somente em `appsettings.local.json`, ignorado pelo Git. O
editor não copia credenciais para nenhum documento do pacote.

## Verificação

`RpaFlow.EditorRoundTrip` abre uma cópia temporária, instancia os 35 blocos, testa
busca/picker/policy, publica por todas as APIs e comprova CAS e round-trip.
