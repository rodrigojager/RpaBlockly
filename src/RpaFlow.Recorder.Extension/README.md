# RpaBlockly Recorder V2

Extensão Chrome Manifest V3 que grava interações consentidas, permite revisão
local e baixa um único `.rpablockly.zip`. O diretório `package/` do bundle já é
um pacote V2 oficial; a extensão não gera script de replay nem depende de
companion app.

## Pré-requisitos

- Chrome 116 ou superior;
- Node.js 24 para desenvolvimento;
- npm com acesso ao registry durante a instalação das dependências.

## Build e validação

```powershell
cd C:\caminho\para\Base-RPA-Blockly\src\RpaFlow.Recorder.Extension
npm ci --ignore-scripts
npm run check
```

O build unpacked fica em `build/`. Ele é recriado do zero, não contém sourcemaps,
CDN ou código remoto e não deve ser versionado.

Antes de validar ou compilar, os schemas JSON são transformados em validadores
standalone e incluídos estaticamente no bundle. A extensão não executa
`ajv.compile`, `eval` ou `Function` dinâmica dentro do Chrome; o build falha se
qualquer avaliação incompatível com a CSP do Manifest V3 reaparecer.

Para produzir o ZIP reproduzível e conferir o inventário de licenças:

```powershell
npm run licenses
npm run release
```

O release compila duas vezes e exige igualdade byte a byte. O ZIP vai para
`../../artifacts/` e o checksum versionado para `release/`.

## Instalação unpacked

1. Abra `chrome://extensions`.
2. Ative o modo do desenvolvedor.
3. Clique em **Carregar sem compactação**.
4. Selecione a pasta `src/RpaFlow.Recorder.Extension/build`.
5. Fixe a extensão e clique no ícone para abrir o side panel.

No primeiro **Iniciar**, o Chrome solicita acesso opcional a todas as páginas
HTTP(S). Depois da autorização, timeline e evidências atravessam mudanças de
origem sem novo clique. A concessão permanece até ser revogada em
`chrome://extensions`; finalizar uma sessão não a remove. Se o acesso falhar, o
Recorder pausa a sessão e oferece **Restabelecer acesso amplo** em vez de fingir
que continuou capturando.

Ações com representação existente são normalizadas para o catálogo V2. Uma ação
observada sem representação executável entra como pendência bloqueante, com a
proposta de contrato necessária; ela nunca é silenciosamente descartada.
Senhas ficam desligadas por padrão.
No modo recomendado, a pessoa escolhe uma senha e recebe uma chave de recuperação
cifrada; no modo avançado, o opt-in aceita um key ID e uma chave pública RSA/SPKI
de pelo menos 2048 bits. Nenhuma senha ou chave privada é persistida.

Para recuperar localmente o PEM gerado pelo modo recomendado:

```powershell
npm run recover:key -- --package .\chave-recorder.txt --output .\chave-privada.pem
```

## Diagnóstico local

- **O side panel não abre:** confirme Chrome 116+ e recarregue a extensão.
- **A página não grava:** confirme o acesso amplo no aviso do Chrome e não use
  páginas internas como `chrome://` ou `file://`.
- **Iframe não aparece:** um frame cuja cadeia não possa ser validada vira pendência; nunca é
  transformado em seletor presumido.
- **Download não conclui:** a sessão é preservada em estado pausado para nova
  tentativa. Ela só é limpa após confirmação do Chrome ou exclusão explícita.
- **Service worker suspenso:** feche e reabra o side panel; o checkpoint em
  `chrome.storage.session` restaura estado, sequência e eventos não sensíveis.

Para detalhes de segurança, consulte o
[threat model](../../docs/recorder/threat-model.md), o
[manual do cliente](../../docs/recorder/manual-cliente.md), o
[manual do desenvolvedor](../../docs/recorder/manual-desenvolvedor.md) e os ADRs
012 a 018.
