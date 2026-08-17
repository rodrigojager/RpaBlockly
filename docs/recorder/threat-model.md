# Threat model do Recorder

## Ativos

- pacote V2 e sua revisão;
- valores digitados, segredos e uploads;
- screenshots e comentários;
- chave privada do destinatário;
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
| Permissão excessiva | hosts opcionais por origem e gesto; sem `<all_urls>`. |
| Suspensão duplica passos | checkpoint em `storage.session` e IDs determinísticos. |
| ZIP adulterado | integridade por entrada antes do JSON. |
| Zip Slip/Bomb/symlink | inspeção sem extração, limites e rejeição de paths/tipos. |
| Preview altera produção | staging read-only; apply separado por CAS. |
| Conflito sobrescreve revisão | revisão esperada obrigatória. |
| Slideshow executa conteúdo | renderização apenas de imagem local; sem HTML/site/replay. |
| Chave privada chega ao frontend | resolvedor exclusivamente backend. |

## Dados deliberadamente não capturados

Cookies, local/session storage, headers, tráfego de rede, HTML completo, valor de
campo sensível, URL assinada e conteúdo de upload sem consentimento.

## Evidência de revisão

Os gates de contrato, extensão, importador e E2E exercitam cada controle. Uma
falha em segredo, integridade, path, limite ou CAS bloqueia o release.
