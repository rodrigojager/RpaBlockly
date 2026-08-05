# Frontend Blockly compartilhado

Esta pasta contém a interface visual e a cópia local do Blockly 13.1.1. O frontend recebe do microservidor o perfil, a configuração e o fluxo do RPA selecionado; por isso não contém seletores nem valores iniciais específicos de um sistema.

O painel direito mostra o JSON schema 1 interpretado pelo .NET. O workspace exportado preserva a disposição visual dos blocos, mas não é lido em produção.

O JSON de produção preserva `name`, `inputs`, ações e subfluxos. O workspace é a serialização interna do Blockly: preserva blocos, campos, conexões, posições e `block.data`, mas não transporta o `name` nem os `inputs` externos do fluxo. Ao levá-lo para outra sessão, transporte também o JSON correspondente. Consulte o [guia operacional](../../docs/guia-editor-blockly.md) para importação, salvamento, backups e modo sem backend.

O editor permite configurar ações web, cadeias de iframe, condições, interrupção controlada, repetições, arrays e objetos aninhados, subfluxos, screenshots, downloads locais ou UNC e espera de código de uso único por um provider do host. Variáveis administradas pela interface ficam em `Blockly.Variables`; valores capturados durante uma execução ficam em `runtime.*`.

Um download por requisição com POST pode alterar o sistema remoto e deve ser revisado antes do uso. A confirmação final segura só funciona quando o projeto do RPA fornece uma política explícita para o sistema de destino. A caixa **comprovar conclusão e publicar feedback** inclui ou omite, de forma atômica, seletor e texto de sucesso, extração do protocolo, timeout e destinos de resultado. Ela não autoriza o envio: essa permissão pertence ao host.

Licença e avisos da biblioteca local estão em `THIRD_PARTY_NOTICES.md` e `vendor/blockly/LICENSE`.

O catálogo atual possui 35 blocos visuais que representam 32 tipos de ação. A referência de campos, defaults e efeitos está em [catálogo de blocos](../../docs/catalogo-de-blocos.md).
