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

As permissões permanentes são limitadas a `activeTab`, `scripting`, `storage`,
`downloads` e `sidePanel`. Quando o Chrome não expõe a URL pelo `activeTab`, o
painel solicita `tabs` como permissão opcional para identificar a aba ativa. O
código consulta somente essa aba. Sem evidências visuais, o gesto de iniciar
solicita acesso às páginas `http://` e `https://` para atravessar origens. Com
evidências, o painel solicita `<all_urls>` como permissão opcional porque a API
`captureVisibleTab` do Chrome exige literalmente `<all_urls>` ou `activeTab`;
`activeTab` não cobre a sessão depois de navegações. O Recorder continua
rejeitando páginas fora de HTTP(S). A concessão feita pela sessão é registrada no
estado local e retirada ao concluir, excluir ou encerrar com falha. Uma concessão
que já existia antes da sessão não é removida. Não existe acesso permanente a
hosts.

Incidentes ou suspeitas devem incluir versão da extensão, horário, origem afetada
e hash do bundle, nunca senhas, chaves de recuperação ou chaves privadas.
