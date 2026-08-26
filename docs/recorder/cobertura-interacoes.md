# Cobertura de interações do Recorder V2

Este documento separa três situações: ação já executável, evento coalescido em
uma ação equivalente e ação observada que depende de decisão de catálogo. A RC 9
não cria tipos de bloco novos. Quando não existe representação segura, o Recorder
gera uma pendência bloqueante e informa o contrato candidato.

## Ações executáveis com o catálogo atual

| Interação observada | Ação V2 gerada | Regra |
|---|---|---|
| abertura e navegação tradicional | `navigate` | URL sanitizada |
| clique simples | `click` | locator único e executável |
| input, textarea e contenteditable | `fill` | conserva o valor final; segredo depende de opt-in |
| checkbox e radio | `setChecked` | conserva o estado booleano final |
| select simples | `selectOption` | usa o valor da opção |
| tecla sem edição resultante e atalho | `pressKey` | inclui combinações como `Control+Shift+K` |
| clique que abre nova página | `clickAndSwitchPage` | exige clique causal e locator de prontidão |
| troca manual de aba | `switchPage` | seleciona por URL exata sanitizada |
| fechamento da página atual | `closePage` | somente quando a causalidade com a aba atual é segura |
| upload de um arquivo | `upload` | bytes só entram com consentimento |
| clique de download | `download` em modo `click` | associa o evento do Chrome ao clique causal |

Digitação comum, paste seguido de input, `change` final, submit causal e navegação
SPA causada por clique são coalescidos para evitar duplicidade sem perder o efeito
executável. Uma tecla imprimível sem evento de edição correspondente vira
`pressKey`; portanto não desaparece em widgets customizados.

## Pendências que exigem decisão

| Interação detectada | Comportamento da RC 9 | Decisão de catálogo sugerida |
|---|---|---|
| hover sobre controle | pendência bloqueante | criar `hover` |
| rolagem manual | pendência bloqueante | criar `scroll` com alvo e posição |
| clique com botão direito | pendência bloqueante | ampliar `click` ou criar `rightClick` |
| clique duplo | pendência bloqueante | ampliar `click` ou criar `doubleClick` |
| drag-and-drop | pendência bloqueante | criar `dragAndDrop` com origem e destino |
| clique em canvas | pendência bloqueante | criar `clickAt` com coordenadas relativas |
| copy ou cut | pendência bloqueante | criar ações de clipboard com política de dados |
| modificadora isolada | pendência bloqueante | criar `keyDown`/`keyUp` |
| select múltiplo | pendência bloqueante | ampliar `selectOption` para lista de valores |
| upload múltiplo ou limpeza do input | pendência bloqueante | ampliar `upload` para lista, inclusive vazia |
| fechamento de aba em segundo plano | pendência bloqueante | ampliar `closePage` para selecionar a página |

Para reduzir ruído sem ocultar capacidade ausente, eventos contínuos como hover e
scroll geram uma pendência por categoria e documento. A pendência declara que ao
menos uma ocorrência foi observada; nenhuma ação executável é inventada.

## Limitações de instrumentação, sem bloco novo obrigatório

- Shadow root fechado: o site não expõe o alvo interno. O evento gera
  `UNSUPPORTED_CLOSED_SHADOW_ROOT`.
- Iframe sem cadeia validável de frame locators: o script pode estar autorizado,
  mas o replay não possui endereço executável até uma melhoria de autoria de
  locators. O evento gera `CROSS_ORIGIN_FRAME_NOT_CAPTURED`.
- Download sem clique causal comprovável: o bloco `download` já existe, porém a
  associação fica bloqueada para revisão.
- Edição de widget não representada por input, textarea ou contenteditable: a
  ocorrência é bloqueada até definir se `fill`, `select2` ou um bloco novo é o
  contrato correto.
- Atalhos reservados pelo navegador ou pelo sistema operacional podem não chegar
  à página e, portanto, não podem ser observados por uma extensão baseada em
  eventos DOM.

Confirmar uma omissão no painel é uma decisão auditável do usuário. Sem essa
confirmação, qualquer pendência bloqueante impede finalizar o pacote.
Como o schema de evidência atual exige `actionId`, uma interação ainda sem ação
V2 não recebe screenshot associado; criar evidência vinculada diretamente à issue
é outra possível evolução de contrato a decidir junto com os novos blocos.
