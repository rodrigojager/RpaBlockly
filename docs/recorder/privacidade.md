# Privacidade do Recorder V2

## Dados observados

Durante uma sessão ativa, a extensão pode observar a URL sanitizada, tipo de
interação, rótulos e atributos permitidos do elemento, retângulo visual,
estrutura limitada ao redor do alvo e o valor final de campos não sensíveis.
Evidências visuais e conteúdo de upload dependem de opções explícitas.

## Dados que não são coletados

A extensão não lê cookies, local storage, session storage, headers, tráfego de
rede, HTML completo, histórico geral do navegador nem conteúdo de outras origens
sem permissão. Parâmetros de URL com nomes sensíveis são removidos. Valores de
senha não entram em eventos, amostras, logs ou checkpoints.

## Segredos

A captura de segredo é opt-in por sessão. O valor é cifrado imediatamente com
AES-256-GCM; a chave é encapsulada com uma chave pública RSA-OAEP-SHA-256. Apenas
o backend destinatário pode possuir a chave privada. Sem opt-in ou chave válida,
o passo vira uma pendência bloqueante em vez de receber um valor inventado.

## Retenção e compartilhamento

O estado de trabalho permanece local no perfil do Chrome e pode sobreviver à
suspensão do service worker. Ele é removido após download confirmado ou exclusão
explícita. O importador guarda o ZIP aplicado e os mappings como evidência lateral
da revisão; staging incompleto expira e pode ser excluído.

O arquivo exportado deve ser tratado como dado de trabalho: compartilhe somente
com pessoas autorizadas, por canal aprovado, e aplique a política de retenção da
organização. Remova evidências e uploads que não sejam necessários.

## Permissões do Chrome

As permissões permanentes são limitadas a `activeTab`, `scripting`, `storage`,
`downloads` e `sidePanel`. Acesso a hosts é opcional, solicitado por origem e por
gesto do usuário. Não existe `<all_urls>` permanente.

Incidentes ou suspeitas devem incluir versão da extensão, horário, origem afetada
e hash do bundle, nunca senhas ou chaves privadas.
