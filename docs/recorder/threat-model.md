# Threat model do Recorder

## Ativos

- pacote V2 e sua revisão;
- valores digitados, segredos e uploads;
- screenshots e comentários;
- senha, chave de recuperação e chave privada do destinatário;
- permissões temporárias de host;
- staging do importador.

## Fronteiras de confiança

1. página visitada → content script;
2. content script → service worker;
3. estado da extensão → ZIP baixado;
4. ZIP não confiável → backend local;
5. staging validado → package store;
6. backend → provedor de segredo.

## Ameaças e controles

| Ameaça | Controle bloqueante |
|---|---|
| Página envia evento falso | `isTrusted`, schema de mensagem, IDs idempotentes e catálogo fechado. |
| Segredo aparece em texto claro | captura opt-in, criptografia imediata, máscara e busca automatizada. |
| Senha fraca protege a recuperação | mínimo de 12 caracteres com letras e números, PBKDF2-HMAC-SHA-256 com salt e 600.000 iterações e sugestão aleatória. |
| Material de recuperação fica no estado | senha e chave de recuperação permanecem somente no painel; cópia confirmada antes da gravação; nenhuma delas entra em checkpoint ou bundle. |
| Permissão excessiva | nenhum host permanente; `<all_urls>` é opcional, solicitado somente por gesto quando screenshots estão ligados, limitado por código a HTTP(S), rastreado por sessão e retirado ao terminar. |
| Suspensão duplica passos | checkpoint em `storage.session` e IDs determinísticos. |
| ZIP adulterado | integridade por entrada antes do JSON. |
| Zip Slip/Bomb/symlink | inspeção sem extração, limites e rejeição de paths/tipos. |
| Preview altera produção | staging read-only; apply separado por CAS. |
| Conflito sobrescreve revisão | revisão esperada obrigatória. |
| Slideshow executa conteúdo | renderização apenas de imagem local; sem HTML/site/replay. |
| Chave privada fornecida pelo desenvolvedor chega ao frontend | modo avançado recebe somente a chave pública; no modo simples, a chave privada recém-gerada é cifrada em memória e descartada antes da gravação. |

## Dados deliberadamente não capturados

Cookies, local/session storage, headers, tráfego de rede, HTML completo, valor de
campo sensível, URL assinada e conteúdo de upload sem consentimento.

## Evidência de revisão

Os gates de contrato, extensão, importador e E2E exercitam cada controle. Uma
falha em segredo, integridade, path, limite ou CAS bloqueia o release.

## Riscos residuais

- texto pessoal visível fora de campos sensíveis pode aparecer em screenshot;
- uma origem autorizada pode mudar de comportamento depois da gravação;
- a segurança da senha, da chave de recuperação, da chave privada recuperada e
  dos canais de compartilhamento é responsabilidade dos participantes;
- permissões opcionais concedidas pelo usuário permanecem administráveis no
  Chrome até serem revogadas.

Esses riscos são mitigados por revisão visual, mappings explícitos, package
revisionado, política conservadora `strict` e instruções de retenção. Nenhum deles
autoriza captura silenciosa ou relaxamento do manifest.
