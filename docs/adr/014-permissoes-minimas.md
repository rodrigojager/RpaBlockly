# ADR-014 — Acesso contínuo por consentimento explícito

Estado: Aceita

## Contexto

Uma gravação pode atravessar várias origens HTTP(S). A concessão `activeTab` é
revogada quando a aba navega para outra origem; exigir um novo clique nesse ponto
cria uma janela em que ações deixam de ser observadas. A continuidade do roteiro
tem prioridade, desde que o Chrome apresente consentimento explícito e a pessoa
possa revogá-lo.

## Decisão

Manter `activeTab`, `scripting`, `storage`, `downloads` e `sidePanel` como
permissões de API. Declarar `<all_urls>` em `optional_host_permissions`, pois o
Chrome exige `activeTab` ou `<all_urls>` para `captureVisibleTab`. O primeiro clique em **Iniciar** chama
`chrome.permissions.request` diretamente e a sessão só começa se o usuário
aceitar o aviso nativo do Chrome.

A concessão permanece no perfil entre origens e sessões até ser revogada nas
configurações da extensão. Ela não é removida ao concluir ou cancelar uma
gravação. O Recorder continua rejeitando páginas fora de HTTP(S), não lê cookies,
headers, tráfego de rede ou armazenamento da página e só injeta o capturador em
abas pertencentes a uma sessão ativa ou pausada.

Se a injeção falhar, a sessão é pausada imediatamente para não aparentar uma
captura completa. O painel oferece **Restabelecer acesso amplo**; a retomada da
gravação continua sendo uma decisão explícita.

## Alternativas recusadas

- Somente `activeTab`: interrompe a captura em toda mudança de origem.
- Hosts obrigatórios no manifesto: pede o privilégio durante a instalação, fora
  do contexto da ação **Iniciar**.
- `debugger`, `webRequest` ou native messaging: ampliam poder sem necessidade
  para os eventos DOM e de navegação suportados.

## Consequências

O Chrome informa que a extensão poderá ler e alterar dados nos sites visitados.
Esse alcance é necessário para injeção programática contínua, mas aumenta o
impacto potencial de uma vulnerabilidade; CSP sem código remoto, catálogo
fechado, `isTrusted`, sanitização e testes de manifesto permanecem bloqueantes.
Frames cross-origin ainda podem gerar pendência quando não for possível construir
uma cadeia executável de frame locators, apesar de o script estar autorizado.

## Rollback

Revogar **Acesso ao site** em `chrome://extensions`, desativar ou desinstalar a
extensão. Uma sessão em andamento é pausada quando a perda de acesso é detectada.

## Testes e evidências

Lint do manifesto, teste da solicitação opcional, instalação limpa com perfil
novo, navegação entre duas origens sem novo clique, screenshots nas duas origens
e simulação de revogação com pausa segura.
