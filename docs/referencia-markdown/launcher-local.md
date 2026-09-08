# Launcher local do RpaBlockly

O launcher `rpablockly.cmd` concentra diagnóstico, setup, editor, validação e
serviços auxiliares sem exigir que a pessoa memorize comandos `dotnet` ou
`docker compose`. Sem argumentos, ele abre um menu em português.

## Primeiro uso

Na raiz de um clone novo:

```powershell
.\rpablockly.cmd doctor
.\rpablockly.cmd setup
.\rpablockly.cmd editor
```

O setup básico:

1. valida Git, PowerShell e o SDK resolvido por `global.json`;
2. cria `appsettings.local.json` a partir do exemplo somente se estiver ausente;
3. restaura e compila a solução;
4. instala o Chromium usado por Playwright e SpyBrowser.

O setup básico não substitui arquivos locais existentes. O modo Docker atualiza
atomicamente apenas `Runtime.Captcha.ServiceUrl` e `ServiceApiKey`, depois que o
solver passa pela readiness. Configurações e chaves permanecem em arquivos
ignorados e não rastreados pelo Git.

## Comandos

| Comando | Efeito |
| --- | --- |
| `rpablockly.cmd` | Abre o menu interativo. |
| `rpablockly.cmd doctor` | Verifica ferramentas, SDK, projeto e configuração. |
| `rpablockly.cmd setup` | Prepara o ambiente básico com menor uso de disco. |
| `rpablockly.cmd editor` | Compila e abre o editor no projeto escolhido. |
| `rpablockly.cmd validate` | Executa `--validate-only` no RPA escolhido. |
| `rpablockly.cmd captcha-up` | Constrói e inicia o solver Docker completo. |
| `rpablockly.cmd captcha-down` | Para o solver sem apagar sua imagem. |
| `rpablockly.cmd sidecar-up` | Inicia Byparr ou FlareSolverr explicitamente. |
| `rpablockly.cmd sidecar-down` | Para ambos os perfis Cloudflare. |
| `rpablockly.cmd status` | Mostra configuração, modelos e serviços locais. |
| `rpablockly.cmd clean-docker -Force` | Remove containers, volumes e a imagem construída pelo RpaBlockly. |

Use `-Project` para outro RPA dentro do repositório:

```powershell
.\rpablockly.cmd editor -Project rpas\RpaContasPagar
.\rpablockly.cmd validate -Project rpas\RpaContasPagar
```

`abrir-editor.cmd` permanece compatível e delega ao novo launcher.

## Modos de captcha

| Modo | Uso de disco | Capacidade preparada |
| --- | --- | --- |
| `None` | Menor | Não baixa modelos nem cria imagens Docker. |
| `Models` | Aproximadamente 25 MB | Provisiona OCR e classificadores ONNX no cache local. |
| `Docker` | Aproximadamente 1 GB ou mais | Constrói solver com OCR, hCaptcha e Whisper. |

Exemplos:

```powershell
.\rpablockly.cmd setup -CaptchaMode Models
.\rpablockly.cmd setup -CaptchaMode Docker
.\rpablockly.cmd editor -CaptchaMode Docker
```

No modo Docker, o launcher gera uma `CAPTCHA_API_KEY` aleatória em
`services/captcha-solver/.env.local`, aponta o `appsettings.local.json` do
projeto para `127.0.0.1:8855` e aguarda a readiness do serviço. A chave não é
impressa e ambos os arquivos são ignorados pelo Git.

O VLM e o sidecar Cloudflare continuam fora do setup automático porque podem
usar providers externos e produzem efeitos de rede específicos. Inicie o
sidecar somente após configurar allowlist e autorização da ação:

```powershell
.\rpablockly.cmd sidecar-up -SidecarProvider byparr
.\rpablockly.cmd sidecar-up -SidecarProvider flaresolverr
```

## Limpeza

Para remover containers, volumes e a imagem local construída pelo Compose do
RpaBlockly:

```powershell
.\rpablockly.cmd clean-docker -Force
```

O comando usa um ID aleatório persistido no gitdir do worktree, mantendo o nome
Compose estável mesmo quando a pasta do checkout é movida. Ele não opera em
contexto Docker remoto nem remove imagens sidecar compartilhadas ou recursos de
outros projetos. Cache local ONNX, `bin/`, `obj/` e artefatos também não são
apagados automaticamente.

## Worker

O worker SQL não é iniciado automaticamente pelo launcher. Habilitar claim pode
executar efeitos de negócio e exige banco, migrations, intertravamentos e
configuração operacional explícita. Use o procedimento em
[`integracao-worker-banco.md`](integracao-worker-banco.md).
