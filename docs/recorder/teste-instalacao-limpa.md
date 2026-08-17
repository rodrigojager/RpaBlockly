# Roteiro de aceite em instalação limpa

Este roteiro é a evidência exigida para o aceite humano do release candidate. Ele
deve ser executado por uma pessoa que não participou da implementação, em um
perfil novo do Chrome, sem orientação oral dos autores.

## Identificação

- versão: `1.0.0-rc.1`;
- checksum esperado: consultar o `.sha256` versionado;
- sistema operacional e versão do Chrome: preencher no relatório;
- pessoa responsável e data: preencher no relatório.

## Passos

1. baixar ZIP e checksum e verificar SHA-256;
2. instalar a extensão descompactada seguindo apenas o manual do cliente;
3. gravar nome, select, checkbox, SPA, upload omitido e iframe na fixture;
4. pausar, fechar o side panel, reabrir e retomar sem duplicação;
5. revisar timeline, remover uma evidência e baixar um único ZIP;
6. importar no editor seguindo apenas o manual do desenvolvedor;
7. mapear inputs/anexo, validar e aplicar como substituição;
8. executar em `strict` e registrar o resultado;
9. repetir com a alteração controlada de DOM em `fallback`;
10. confirmar que não houve senha, caminho `fakepath`, cookie ou storage no ZIP.

## Critério

Todos os passos devem ser concluídos sem editar JSON e sem consultar a conversa
de desenvolvimento. Dúvidas ou falhas devem ser registradas por etapa, junto da
mensagem exibida e sem dados pessoais. O sign-off humano é externo ao teste
automatizado e não deve ser declarado antes da execução real.
