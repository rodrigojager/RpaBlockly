# Privacidade do Recorder V2

## Dados observados

Durante uma sessão ativa, a extensão pode observar a URL sanitizada, tipo de
interação, rótulos e atributos permitidos do elemento, retângulo visual,
estrutura limitada ao redor do alvo e o valor final de campos não sensíveis.
Evidências visuais e conteúdo de upload dependem de opções explícitas.

## Dados que não são coletados

A extensão não lê cookies, local storage, session storage, headers, tráfego de
rede, HTML completo nem histórico geral do navegador. Parâmetros de URL com nomes
sensíveis são removidos. Valores de
senha não entram em eventos, amostras, logs ou checkpoints.

## Segredos

A captura de segredo é opt-in por sessão. O valor é cifrado imediatamente com
AES-256-GCM; a chave é encapsulada com uma chave pública RSA-OAEP-SHA-256. No
modo simples, o par RSA é gerado localmente e a chave privada é imediatamente
cifrada com uma chave derivada da senha antes de ser exibida como chave de
recuperação. Senha, chave de recuperação e chave privada não são salvas pela
extensão nem entram no bundle. No modo avançado, somente a chave pública
fornecida entra na extensão. Sem opt-in ou material válido, o passo vira uma
pendência bloqueante em vez de receber um valor inventado.

## Retenção e compartilhamento

O estado de trabalho permanece local no perfil do Chrome e pode sobreviver à
suspensão do service worker. Ele é removido após download confirmado ou exclusão
explícita. O importador guarda o ZIP aplicado e os mappings como evidência lateral
da revisão; staging incompleto expira e pode ser excluído.

O arquivo exportado deve ser tratado como dado de trabalho: compartilhe somente
com pessoas autorizadas, por canal aprovado, e aplique a política de retenção da
organização. Remova evidências e uploads que não sejam necessários.

## Permissões do Chrome

As permissões de API são `activeTab`, `scripting`, `storage`, `downloads` e
`sidePanel`. A origem especial `<all_urls>` é opcional: o Recorder a
solicita no primeiro **Iniciar**, dentro do gesto do usuário, e não inicia se o
aviso nativo do Chrome for recusado. A concessão permite injetar o capturador e
usar `captureVisibleTab` de forma contínua ao atravessar sites. Ela permanece no
perfil entre sessões até ser revogada em `chrome://extensions`.

O alcance da permissão não altera os dados coletados: o content script só é
injetado em abas de uma sessão ativa ou pausada, aceita eventos confiáveis e
aplica a sanitização descrita acima. O Recorder rejeita páginas fora de HTTP(S).
Se o acesso for perdido, a sessão é pausada e o painel mostra uma recuperação
explícita; não há continuidade aparente com eventos ausentes.

Incidentes ou suspeitas devem incluir versão da extensão, horário, origem afetada
e hash do bundle, nunca senhas, chaves de recuperação ou chaves privadas.
