# Instruções do projeto

- Preserve todos os arquivos de texto em Unicode UTF-8.
- Não introduza mojibake nem use entidades HTML no lugar de `á`, `é`, `ó`, `ã`, `õ` e `ç`.
- Mantenha Blockly, JSON schema 1, validadores e handlers sincronizados.
- Não versione segredos, tokens, senhas, cookies, estados autenticados ou strings de conexão reais.
- Não execute efeitos irreversíveis sem autorização explícita.
- O worker escolhe e reserva o caso; o fluxo Blockly processa apenas o caso recebido.
