# Guia operacional do Editor Blockly

## O que o editor manipula

O editor mantém três representações diferentes:

1. **Workspace Blockly:** blocos, conexões e posições visuais.
2. **JSON schema 1 de produção:** roteiro exibido no painel direito e interpretado pelo .NET.
3. **Configuração local:** campos declarados em `rpa.editor.json` e valores de `Blockly.Variables`.

Somente `flow.production.json` é executado em produção. O workspace serve para preservar o layout visual e não substitui o roteiro.

## Pré-requisitos do projeto

A pasta do RPA precisa conter, antes de abrir o editor:

- `rpa.editor.json`;
- o `.csproj` indicado em `projectFile`;
- o arquivo indicado em `configurationFile`;
- o arquivo indicado em `flowFile`.

O microservidor não cria esses arquivos ausentes. Os caminhos do perfil são resolvidos dentro da pasta do RPA e não podem escapar dela.

Exemplo de perfil:

```json
{
  "displayName": "Meu RPA",
  "projectFile": "MeuRpa.csproj",
  "configurationFile": "appsettings.local.json",
  "flowFile": "flow.production.json",
  "configurationFields": [
    {
      "path": "Input.Url",
      "label": "URL inicial",
      "source": "input.url",
      "type": "url"
    },
    {
      "path": "Runtime.Headless",
      "label": "Executar sem interface",
      "type": "checkbox"
    }
  ]
}
```

Tipos de campo aceitos no perfil: `text`, `url`, `email`, `password`, `date`, `number`, `checkbox` e `stringList`. `nullable: true` permite `null`. A propriedade `source` informa ao editor o caminho de dados associado, mas o valor continua salvo na configuração, não no fluxo.

## Abrir

Na raiz do workspace:

```powershell
.\abrir-editor.cmd examples\RpaExemplo
.\abrir-editor.cmd rpas\MeuRpa
.\abrir-editor.cmd rpas\OutroRpa
```

Sem argumento, o atalho abre `examples\RpaExemplo`. Também existem:

```powershell
.\abrir-editor.cmd rpas\MeuRpa
.\abrir-editor.cmd rpas\OutroRpa
```

O comando inicia o microservidor local, normalmente em `http://127.0.0.1:5187`, abre o navegador e mantém um console ativo. `Ctrl+C` ou o fechamento desse console encerra o editor.

Execução direta:

```powershell
dotnet run --project src/RpaFlow.Editor/RpaFlow.Editor.csproj -- `
  --project-root C:\caminho\da\base\rpas\MeuRpa
```

Opções adicionais:

- `--no-open`: não abre o navegador automaticamente;
- `--url http://127.0.0.1:5187`: altera o endereço local;
- `--configuration <caminho>`: substitui o arquivo do perfil;
- `--flow <caminho>`: substitui o fluxo do perfil.

O servidor aceita apenas loopback, não habilita CORS e exige um token aleatório nas APIs de leitura e gravação.

## Carregamento inicial

Ao conectar, o editor:

1. carrega e valida `rpa.editor.json`;
2. lê configuração e fluxo;
3. valida ambos no microservidor;
4. converte o JSON de produção em blocos;
5. exibe o perfil e os campos específicos do RPA;
6. guarda uma fotografia inicial do fluxo para o botão de restauração.

O nome do fluxo e a lista `inputs` não possuem formulário próprio. Eles são preservados do JSON carregado ou importado.

## Criar e editar um roteiro

Não existe atualmente um botão **Novo fluxo**. Para um RPA novo, copie o template ou prepare primeiro um `flow.production.json` válido.

Depois de abrir:

1. arraste blocos da toolbox;
2. conecte-os na ordem de execução;
3. mantenha exatamente uma sequência principal;
4. deixe cada bloco **Definir subfluxo** como uma raiz separada;
5. coloque ações condicionais e repetidas dentro de `THEN`, `ELSE` ou `DO`;
6. acompanhe o JSON e a mensagem de validação no painel direito.

Um bloco comum solto cria uma segunda raiz e invalida o documento.

## Toolbox compartilhada

A toolbox é a mesma para todos os RPAs desta base. `rpa.editor.json` troca arquivos e campos de configuração, mas não esconde blocos.

Atualmente existem 35 blocos visuais:

- **Navegação e cliques:** navegar, clicar, clicar se visível, clicar e assumir nova aba, assumir aba existente e fechar aba atual;
- **Esperas:** aguardar elemento e aguardar página estável;
- **Dados e anexos:** preencher, selecionar opção nativa, definir marcação, pressionar tecla, digitar sequencialmente, digitar em inputs segmentados, anexar, preservar ou preencher, Select2, campo monetário, definir variável, transformar caminho, capturar instante UTC, aguardar código de uso único, ler elemento e ler vários elementos;
- **Condições e repetições:** se valor, se elemento, interromper com erro, repetir e para cada item;
- **Subfluxos:** executar subfluxo e definir subfluxo;
- **Arquivos, evidência e segurança:** screenshot, download após clique, download por requisição e confirmação final segura.

Esses blocos representam 32 `action.type`. Consulte [Catálogo de blocos](catalogo-de-blocos.md).

### Feedback da confirmação final

No bloco **confirmação final segura**, marque **comprovar conclusão e publicar feedback** quando a política autorizada do portal precisar validar a mensagem final, extrair o protocolo e publicar os resultados em `runtime.*`. Todos os critérios exibidos passam a ser obrigatórios e são salvos juntos.

Desmarque a caixa para manter o bloco no formato seguro legado, sem publicar conclusão. Essa escolha não concede nem revoga autorização de envio: o host seguro continua cancelando a confirmação, e somente uma configuração externa protegida pode aceitar o efeito irreversível.

## Botões e persistência

### Salvar fluxo de produção

O botão:

1. converte os blocos para JSON;
2. valida no frontend;
3. envia ao microservidor;
4. valida com `RpaFlow.Contracts`;
5. grava em UTF-8 sem BOM usando arquivo temporário;
6. mantém a versão anterior em `<fluxo>.bak`.

O salvamento preserva a semântica, mas não a posição visual dos blocos.

### Configuração e variáveis

O diálogo permite alterar os campos declarados no perfil e as variáveis de `Blockly.Variables`.

Variáveis personalizadas podem ser:

- texto;
- número;
- booleano;
- nulo;
- lista JSON;
- objeto JSON.

A chave `nomeDaChave` fica disponível como `config.nomeDaChave`. Adicionar, editar ou remover uma variável modifica somente a memória até usar **Salvar configuração** ou **Salvar tudo**. O botão **Cancelar** apenas fecha o diálogo: ele não desfaz essas mudanças em memória, que ainda podem ser persistidas por um **Salvar tudo** posterior. Para descartá-las com segurança, feche e reabra o editor sem salvar a configuração.

Segredos podem aparecer como campos `password` do appsettings local, mas nunca são copiados automaticamente para `flow.production.json`. Não crie `Blockly.Variables` para senhas, tokens ou strings de conexão.

### Salvar tudo

O botão salva primeiro a configuração e depois o fluxo. Não é uma transação única:

- se a configuração falhar, o fluxo não é tentado;
- se a configuração for gravada e o fluxo falhar, a configuração permanece gravada.

### Restaurar fluxo salvo

O nome atual do botão exige atenção: ele restaura a fotografia carregada no início da sessão.

Ele não:

- relê o arquivo do disco;
- usa o `.bak`;
- passa a apontar para a última versão depois de salvar.

Também descarta alterações visuais sem confirmação. Para reler o arquivo gravado mais recentemente, feche e reabra o editor.

## Importar fluxo de produção

**Importar fluxo de produção** lê um JSON escolhido e o converte em blocos. O arquivo precisa possuir:

- `schemaVersion: 1`;
- `actions` não vazio;
- somente ações compreendidas pelo editor;
- estrutura que possa formar uma única sequência principal e definições separadas de subfluxo.

A importação altera apenas o workspace em memória. Use depois **Salvar fluxo de produção** ou **Salvar tudo** para persistir no projeto.

O workspace atual é limpo antes de a conversão terminar. Se o arquivo falhar no meio da importação, o editor pode ficar vazio ou parcialmente preenchido. Preserve o baseline e exporte o workspace antes de importar um arquivo incerto.

Uma importação concluída confirma apenas o parse, as verificações estruturais do frontend e a conversão para blocos. Ela não equivale à validação do contrato/runtime feita pelo backend ao salvar nem a `--validate-only`.

## Exportar e importar workspace

**Exportar workspace** baixa a serialização interna do Blockly, incluindo blocos, conexões, campos e posição. **Importar workspace** limpa o editor e restaura essa serialização.

O workspace:

- não é executável;
- não é salvo pelo microservidor;
- não substitui `flow.production.json`;
- não transporta o nome do fluxo nem `inputs`;
- deve ser exportado separadamente quando o layout for importante.

Ao importar um workspace em outra sessão, o JSON gerado usa o nome e os `inputs` do fluxo que já estava carregado naquela sessão. Transporte sempre o JSON de produção junto com o workspace.

A importação de workspace também limpa o estado atual antes de terminar e não possui rollback.

## Backups

O microservidor mantém uma versão anterior por arquivo:

```text
flow.production.json.bak
appsettings.local.json.bak
```

Não existe histórico de múltiplas versões nem botão de restauração do `.bak`.

Em caso de regressão:

1. não salve novamente;
2. feche o editor;
3. preserve cópias do arquivo principal e do `.bak`;
4. substitua manualmente o principal por uma cópia do `.bak`;
5. reabra o editor;
6. execute `--validate-only` e o teste de round-trip.

## Falha de conexão e modo local sem microservidor

Se `/api/session` não responder, o frontend entra integralmente em **Modo local sem backend** e carrega um fluxo interno mínimo.

Nesse modo:

- blocos e JSON ao vivo continuam funcionando;
- importação de fluxo funciona;
- exportação e importação de workspace funcionam;
- salvar fluxo usa o seletor nativo de arquivo, quando disponível, ou baixa `flow.production.json`;
- configuração não pode ser carregada nem salva;
- **Salvar tudo** falha antes de salvar o fluxo;
- não há `.bak`;
- não há validação final do microservidor/runtime.

Se `/api/session` responder, mas o perfil, a configuração ou o fluxo falhar depois, a interface também pode exibir o fallback mínimo enquanto ainda mantém a sessão do servidor. Nesse estado parcial, **Salvar fluxo de produção** pode tentar um `PUT` no backend; não trate a tela como modo offline confiável. Feche o editor, corrija a causa e reabra antes de editar ou salvar.

Para trabalho normal no workspace, use sempre o atalho com microservidor e confirme que o perfil, a configuração e o fluxo esperados foram carregados.

## Round-trip JSON ⇄ Blockly

```text
flow.production.json
  → JSON para blocos
  → edição visual
  → blocos para JSON
  → validação
  → flow.production.json
```

IDs existentes ficam em `block.data`. Condições, loops e subfluxos são convertidos recursivamente. A equivalência é semântica, não textual: o editor pode reordenar propriedades, reformatar JSON e omitir propriedades cujo valor seja o padrão compatível.

Não acrescente manualmente uma propriedade que a interface não represente. Ela pode ser rejeitada ou perdida na volta; hoje, por exemplo, o runtime aceita `navigate.timeoutMs`, mas o bloco de navegação não o expõe.

Execute depois de qualquer mudança em bloco, propriedade, conversão ou schema:

```powershell
.\tests\run-editor-roundtrip.ps1
```

O teste cobre os três fluxos de produção e uma fixture com propriedades generalizadas.

## Compatibilidade de defaults

Alguns defaults visuais novos são mais estritos ou convenientes, enquanto JSONs antigos preservam o comportamento anterior:

| Campo | JSON antigo sem propriedade | Novo bloco visual |
| --- | --- | --- |
| `wait.matchMode` | `first` | `single` |
| Condição de elemento `matchMode` | `first` | `single` |
| `select2.comparison` | comportamento legado | `caseInsensitive` |
| `typeSequentially.clearFirst` | `false` | `true` |
| `typeSequentially.blurAfter` | `false` | `true` |
| `repeat.indexVariable` | `repeatIndex` | `repeatIndex`, editável |
| `fillMaskedCurrency` | 2 casas, 30 ms, `Tab` | mesmos valores |

Ao importar um JSON antigo, o editor mantém os defaults legados aplicáveis. Ao arrastar um bloco novo, usa os defaults visuais.

## Cuidados adicionais

- Renomear uma variável atualiza referências reconhecidas pela rotina do editor; revise o JSON, principalmente corpos/cabeçalhos de requisição e destinos de artefatos.
- Importação não é uma operação transacional.
- Salvar fluxo não salva layout.
- Workspace não salva `name` nem `inputs`.
- O botão de restauração não é o backup.
- O modo sem backend não é equivalente ao editor conectado.
