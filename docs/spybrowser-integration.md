# Integração SpyBrowser

## Configuração

O runtime seleciona `spybrowser` por padrão. As opções de interação compatíveis com versões anteriores são `SpyBrowserHumanize: true`, `SpyBrowserMouseAlgorithm: "bezier"` e `SpyBrowserCompatibilityMode: "legacy"`. `cursory` e `bezier` são algoritmos válidos; os modos válidos são `legacy` e `playwrightcompatible` (normalizados sem distinção entre maiúsculas e minúsculas).

No editor, abra **Configuração** para editar as opções. Os controles de humanização aparecem somente para SpyBrowser; algoritmo e compatibilidade ficam inativos quando a humanização é desligada. Perfis antigos sem os novos campos recebem defaults compatíveis na interface, sem reescrever campos não relacionados. Aliases de navegador já salvos são mantidos ao carregar/salvar.

Quando outro navegador está selecionado, as opções SpyBrowser não são aplicadas. Com `SpyBrowserHumanize: false`, algoritmo e modo ficam inativos e não são validados nem usados. Valores ativos inválidos impedem a inicialização antes de criar o navegador.

## Homologação assistida

Ao abrir a homologação, os controles são preenchidos pela configuração runtime persistida do projeto/sessão. O endpoint continua aceitando clientes antigos: overrides SpyBrowser omitidos ou `null` herdam os valores da configuração do runtime. A interface envia override de algoritmo/modo apenas para SpyBrowser com humanização habilitada; para outros navegadores omite os três campos. Com humanização desligada, envia somente `spyBrowserHumanize: false`.

## Compatibilidade de interação

`legacy` mantém os comportamentos anteriores. `playwrightcompatible` preserva operações Playwright nativas relevantes, incluindo `Fill`, atalhos e duplo clique; não transforma `Fill` em digitação cadenciada. Essa opção altera a compatibilidade do roteamento de interação, não a semântica da ação Blockly.

## Distribuição e bootstrap

Os pacotes `SpyBrowser.Core`, `SpyBrowser.Cursory` e `SpyBrowser.Playwright` são obtidos exclusivamente dos assets públicos do release GitHub `v0.2.0-beta.2.1` e verificados por SHA256 antes de serem colocados no feed local ignorado `artifacts/spybrowser-feed`. `Directory.Build.props` adiciona esse feed de forma relativa à raiz do checkout; também se aplica a templates externos que importem projetos deste repositório. O launcher e os jobs .NET/SQL fazem bootstrap antes do restore/build; doctor, status e limpeza não baixam pacotes.

`tools/spybrowser-packages.lock.json` fixa a versão, o commit de origem e SHA-256 dos três pacotes. O bootstrap rejeita hashes ausentes ou divergentes, baixa para arquivos temporários e só depois promove o conteúdo verificado ao feed local. A rotina aceita `-FixtureBaseUri` somente para HTTP loopback em testes, mantendo a URL de produção fixada no GitHub. A licença do dataset foi atestada pelo operador; isso não representa revisão jurídica independente. A publicação no NuGet não foi habilitada.

## Validação local

Execute o caminho focado sem chamar CAPTCHA nem provedores externos:

```powershell
$env:TEMP = 'D:/RpaBlockly/artifacts/spybrowser-ui-integration/tmp'
$env:TMP = $env:TEMP
./tools/Restore-SpyBrowserPackages.ps1
dotnet restore tests/RpaFlow.SpyBrowserIntegrationChecks/RpaFlow.SpyBrowserIntegrationChecks.csproj
dotnet run --project tests/RpaFlow.SpyBrowserIntegrationChecks/RpaFlow.SpyBrowserIntegrationChecks.csproj --no-restore -- --browser
```

Os casos de navegador usam páginas locais e verificam wrapping, preenchimento DOM, clique e ponto final de movimento. O caminho focado contém 11 grupos de assertions e três casos reais de navegador; serviços externos de CAPTCHA/provedores não são usados. `Humanize=false` também é verificado sem wrappers. O round-trip completo do editor valida persistência, exclusividade dos controles por navegador e compatibilidade com configurações antigas.

O bootstrap também passou teste com servidor HTTP de loopback e lock/bytes de fixture: download com hash válido, sucesso de cache sem novo download, rejeição após três tentativas de hash incorreto e rejeição fail-closed dos hashes `PLACEHOLDER`. A sintaxe PowerShell foi analisada e `node --check` passou. Estes testes de fixture não validam nem substituem os hashes/bytes reais do release.
