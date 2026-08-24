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
- homologação assistida com navegador visível, progresso e evidências;
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
6. abra **Validar roteiro** e escolha a última etapa segura;
7. execute no Chromium ou CloakBrowser, revise cards e screenshots e corrija o
   rascunho até o limite ser alcançado;
8. salve o pacote;
9. reabra e confira a nova revisão;
10. execute o host com `--validate-only`.

## Validar roteiro visualmente

A homologação usa o rascunho que está na tela, inclusive mudanças ainda não
salvas, mas exige que a revisão de origem continue atual. O backend repete a
validação oficial e cria um snapshot imutável apenas para aquela execução.

O limite é inclusivo: a ação escolhida é executada e o runtime encerra antes da
ação seguinte. Somente ações-folha aparecem na lista. Se a ação estiver em um
ramo que não for percorrido, o término sem alcançar o limite é tratado como
falha, não como sucesso.

Durante a execução:

- o navegador permanece visível;
- o bloco ativo é destacado no Blockly;
- cada ação recebe card com estado e duração;
- **Parar agora** cancela o token e fecha o contexto do navegador;
- screenshots por etapa mascaram campos de formulário, conteúdo editável e
  elementos marcados como privados, inclusive em iframes e shadow roots abertos;
- falhas geram evidência auxiliar sem substituir a causa original.

O painel nunca devolve valores de `input.*`, `config.*`, `attachments.*` ou
`runtime.*`. Imagens ficam em `artifacts/homologacao-editor` e devem seguir a
retenção e o controle de acesso do ambiente. Reabrir o diálogo reconecta à
execução mais recente da sessão local.

Esta função é para homologação assistida. Banco, claim, retry, persistência de
resultado e ações autorizadas em produção continuam sob responsabilidade do
worker.

## Configuração

O diálogo de configuração edita o JSON indicado por `configurationFile` no perfil.
Segredos podem existir somente em `appsettings.local.json`, ignorado pelo Git. O
editor não copia credenciais para nenhum documento do pacote.

## Verificação

`RpaFlow.EditorRoundTrip` abre uma cópia temporária, instancia os 35 blocos, testa
busca/picker/policy, executa homologação com screenshot, limite e cancelamento,
publica por todas as APIs e comprova CAS e round-trip.
