# Roteiro de aceite em instalação limpa

Este roteiro é a evidência exigida para o aceite humano do release candidate. Ele
deve ser executado por uma pessoa que não participou da implementação, em um
perfil novo do Chrome e sem orientação oral dos autores. O resultado deve ser
registrado em [relatorio-instalacao-limpa.md](relatorio-instalacao-limpa.md).

## Identificação do candidato

- RpaBlockly: `2.0.0-rc.1`;
- Recorder: `1.0.0-rc.1`;
- checksum esperado: consultar
  `src/RpaFlow.Recorder.Extension/release/rpablockly-recorder-1.0.0-rc.1.zip.sha256`;
- estado obrigatório antes do teste: `release candidate`, REC-140 pendente.

## Pré-requisitos da pessoa avaliadora

- clone limpo desta branch, em pasta própria;
- .NET SDK indicado por `global.json`;
- PowerShell 7;
- Chrome em uma versão suportada por `docs/adr/013-chrome-minimo.md`;
- perfil novo do Chrome, sem extensões adicionais;
- Chromium do Playwright instalado pelo comando abaixo;
- ZIP e checksum produzidos pela mesma execução de CI.

No GitHub Actions, baixe o artefato `release-gates-windows-latest` do job Windows
da branch avaliada. Dentro dele estão o ZIP do Recorder e o `.sha256`. Como
alternativa local, gere os mesmos bytes com:

```powershell
npm ci --ignore-scripts --prefix src/RpaFlow.Recorder.Extension
npm run release --prefix src/RpaFlow.Recorder.Extension
```

Prepare o navegador do runtime e uma área descartável, ignorada pelo Git:

```powershell
dotnet restore RpaBlockly.slnx
dotnet build RpaBlockly.slnx --configuration Release
pwsh src/RpaFlow.Playwright/bin/Release/net9.0/playwright.ps1 install chromium
.\tools\Preparar-Aceite-Recorder.ps1
```

Se `tmp/recorder-acceptance` já existir, escolha outro destino. Não reutilize uma
execução anterior.

## 1. Verificar e instalar a extensão

1. compare o hash do ZIP com o `.sha256`:

   ```powershell
   (Get-FileHash .\rpablockly-recorder-1.0.0-rc.1.zip -Algorithm SHA256).Hash.ToLowerInvariant()
   ```

2. descompacte o ZIP em uma pasta vazia;
3. no perfil novo, abra `chrome://extensions`, ative o modo do desenvolvedor e
   selecione **Carregar sem compactação**;
4. escolha a pasta que contém `manifest.json` e fixe o ícone do Recorder;
5. registre no relatório o hash, a versão do Chrome e se o Chrome exibiu algum
   aviso ou permissão diferente do manual do cliente.

## 2. Iniciar a fixture original

Em um terminal que permanecerá aberto, execute:

```powershell
dotnet run --project tools/RpaFlow.RecorderFixture
```

Abra `http://127.0.0.1:5178/index.html` no perfil novo do Chrome. A ferramenta
escuta somente em loopback e informa `Modo: DOM original para execução strict`.

## 3. Gravar a jornada

1. abra o side panel pelo ícone da extensão;
2. use o nome `Aceite independente Recorder V2`;
3. mantenha **Capturar evidências visuais** ligada;
4. mantenha captura de segredos desligada;
5. mantenha inclusão dos bytes de uploads desligada;
6. aceite o aviso e inicie a gravação, concedendo acesso apenas a
   `http://127.0.0.1:5178/*`;
7. na fixture, nesta ordem:
   - preencha **Nome completo** com `Maria da Silva`;
   - selecione **São Paulo**;
   - marque **Aceito os termos**;
   - escolha `tmp/recorder-acceptance/arquivo-aceite.txt` no upload;
   - clique em **Ação dinâmica**;
   - clique no primeiro **Editar item**;
8. pause a gravação, feche o side panel, reabra-o e confirme que a sessão continua
   pausada e sem duplicar passos;
9. retome a gravação e:
   - clique em **Confirmar no shadow DOM**;
   - clique em **Executar no iframe**;
   - clique em **Avançar na SPA**;
10. não digite nem clique no campo de senha;
11. escolha **Revisar e baixar**, confira a timeline, remova uma evidência e
    conclua um único download `.rpablockly.zip`.

Falha, duplicação, perda da sessão, permissão ampla ou passo ausente reprova a
etapa. Não edite o ZIP.

## 4. Verificar privacidade e estrutura do bundle

Execute sobre o arquivo baixado:

```powershell
.\tools\Verificar-Privacidade-Bundle-Recorder.ps1 `
  -Bundle .\Aceite-independente-Recorder-V2.rpablockly.zip
```

O nome real pode variar. O comando precisa confirmar os documentos obrigatórios,
UTF-8 válido e ausência de `fakepath`, `document.cookie`, `localStorage` e
`sessionStorage`, além de captura de segredos e bytes de upload desabilitada.
Registre o SHA-256 exibido. A pessoa avaliadora também deve confirmar na timeline
que nenhuma ação de senha foi criada e que o upload contém metadados, mas não os
bytes do arquivo.

## 5. Importar sem editar JSON

Em outro terminal, abra o editor sobre a área descartável:

```powershell
dotnet run --project src/RpaFlow.Editor -- `
  --project-root tmp/recorder-acceptance
```

1. clique em **Importar Recorder** e inspecione o bundle;
2. confira origem, timeline, pendências e evidências;
3. escolha **Substituir pacote**;
4. associe os caminhos pela natureza do valor, usando esta tabela:

| Valor mostrado na gravação | Destino no editor |
|---|---|
| URL inicial `http://127.0.0.1:5178/index.html` | `input.url` |
| `Maria da Silva` | `input.nome` |
| `SP` | `input.estado` |
| `true` do checkbox | `input.aceite` |
| referência do upload | `attachments.arquivo` |

Os nomes de origem começam por `input.recorded.*` ou `attachments.recorded.*` e
podem conter número de etapa. Não mapeie pela posição sem antes conferir a
timeline e o tipo.

5. valide a decisão e aplique por compare-and-swap;
6. feche e reabra o editor; confirme que a revisão publicada, os blocos, os
   localizadores e a policy `strict` foram preservados;
7. abra **Configuração local** e confira os valores já preparados nos controles
   tipados. Não edite JSON.

## 6. Executar em strict

Com a fixture original ainda ativa, execute:

```powershell
dotnet run --project examples/RpaExemplo -- `
  --config tmp/recorder-acceptance/appsettings.local.json `
  --package-store tmp/recorder-acceptance/package-store `
  --rpa-id rpa-exemplo
```

A execução deve concluir todas as ações e a fixture deve exibir os resultados das
interações. Registre a revisão e a quantidade de ações exibidas pelo host.

## 7. Executar com DOM alterado em fallback

1. no editor, em **Política de resiliência**, selecione
   **Fallback — candidatos exatos em ordem**;
2. clique em **Aplicar política ao rascunho** e depois em **Salvar pacote**;
3. encerre a fixture original com `Ctrl+C`;
4. no mesmo terminal, inicie a alteração controlada:

   ```powershell
   dotnet run --project tools/RpaFlow.RecorderFixture -- --changed-dom
   ```

5. confirme a mensagem `Modo: DOM alterado para comprovar fallback`;
6. repita exatamente o comando de execução da etapa anterior.

O servidor remove somente o `data-testid` primário de **Ação dinâmica**. A mesma
revisão funcional deve concluir usando um candidato exato alternativo. Não grave
um segundo bundle e não edite os documentos JSON.

## Critério de aceite

Todas as etapas devem ser concluídas sem editar JSON, sem consultar a conversa de
desenvolvimento e sem orientação oral. O relatório deve conter:

- identidade da pessoa independente, data, sistema operacional e Chrome;
- branch, commit, artefato e SHA-256;
- resultado de cada etapa;
- mensagens e evidências sem dados pessoais;
- decisão explícita `APROVADO` ou `REPROVADO`.

O sign-off humano é externo ao teste automatizado. Enquanto o relatório
permanecer `PENDENTE`, a versão continua RC e não pode ser declarada GA.
