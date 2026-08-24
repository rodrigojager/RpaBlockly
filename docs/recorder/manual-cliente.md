# Manual do cliente — Recorder V2

O Recorder transforma interações feitas por uma pessoa no Chrome em um único
arquivo `.rpablockly.zip`. O arquivo contém um pacote V2 nativo, a linha do tempo,
pendências, evidências consentidas e metadados de integridade. Ele não contém um
script autônomo de replay.

## Instalar

1. obtenha `rpablockly-recorder-1.0.0-rc.6.zip` e o arquivo `.sha256` da mesma release;
2. confira o SHA-256 antes de descompactar;
3. abra `chrome://extensions`, ative o modo do desenvolvedor e escolha
   **Carregar sem compactação**;
4. selecione a pasta descompactada e fixe o ícone do Recorder;
5. abra uma página comum `http://` ou `https://` e clique no ícone.

No PowerShell, a verificação do arquivo é:

```powershell
(Get-FileHash .\rpablockly-recorder-1.0.0-rc.6.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

O valor deve coincidir com o conteúdo do `.sha256` distribuído na release.

## Gravar um roteiro

1. informe um nome claro para a gravação;
2. escolha se deseja evidências visuais e conteúdo de uploads;
3. leia e aceite o aviso de privacidade;
4. clique em **Iniciar** e autorize o acesso temporário solicitado; com
   evidências visuais, o Chrome mostra uma autorização ampla porque sua API de
   screenshot exige `<all_urls>`, embora o Recorder só aceite páginas HTTP(S);
5. navegue normalmente, usando cliques, campos, checkbox, radio, select, upload,
   SPA, outros sites, novas páginas e iframes acessíveis;
6. use **Pausar** se precisar fazer algo que não deve entrar no roteiro;
7. escolha **Revisar e baixar**, resolva as pendências e confira a timeline;
8. remova evidências desnecessárias e conclua o download.

A sessão só é apagada automaticamente depois que o Chrome confirma o download.
Cancelar remove explicitamente a sessão local. O acesso concedido pela sessão é
retirado nos dois casos e também quando a gravação termina com falha.

## Senhas, uploads e evidências

Senhas ficam desligadas por padrão. Ao ativar a opção, mantenha o modo
**Senha e chave de recuperação** e:

1. digite uma senha de ao menos 12 caracteres, com letras e números, ou clique
   em **Gerar senha**;
2. clique em **Gerar chave de recuperação**;
3. copie a senha e a chave de recuperação para um local seguro;
4. confirme a cópia e somente então inicie a gravação.

A extensão não salva esses dois dados e não consegue recriá-los depois que o
painel é fechado. Repasse ambos ao desenvolvedor, preferencialmente por canais
separados. A chave de recuperação contém uma chave privada RSA cifrada; sozinha,
sem a senha, ela não abre os segredos.

O modo avançado, que solicita ID e chave pública RSA em PEM, só é necessário
quando o desenvolvedor já forneceu esses dados. Nos dois modos, cada valor é
cifrado imediatamente com AES-256-GCM, e a chave simétrica é encapsulada com
RSA-OAEP-SHA-256.

Uploads registram nome, tipo, tamanho e hash. Os bytes só entram no ZIP quando a
opção correspondente foi marcada. Extensões perigosas e arquivos acima dos
limites são recusados.

Antes de um screenshot, a extensão mascara campos sensíveis. Ainda assim, revise
cada imagem: informações visíveis fora dos campos podem ser pessoais.
O painel informa quantas capturas foram salvas e mostra uma mensagem explícita
quando o Chrome recusa ou não consegue processar uma evidência.

## O que entregar ao desenvolvedor

Entregue o `.rpablockly.zip` final. Se a captura de senhas estava ligada no modo
simples, entregue também a senha e a chave de recuperação pelos canais aprovados,
preferencialmente separados entre si e do bundle. Informe quais entradas,
segredos e anexos devem alimentar as referências exibidas pelo importador. Não
edite o ZIP manualmente: qualquer alteração invalida a integridade.

Consulte também [privacidade](privacidade.md) e
[solução de problemas](troubleshooting.md).
