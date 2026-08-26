# Documentação da RpaBlockly V2

Comece pelo [README da raiz](../README.md). A documentação operacional da V2 é:

- [pacote operacional](v2/pacote-operacional.md): os três documentos, revisão e stores;
- [editor Blockly](referencia-markdown/guia-editor-blockly.md): abrir, editar e resolver conflitos;
- [diagrama da homologação assistida](diagramas/homologacao-assistida.drawio);
- [catálogo de blocos](referencia-markdown/catalogo-de-blocos.md): 36 blocos e 33 ações;
- [como adicionar um bloco](referencia-markdown/como-adicionar-bloco.md): sincronização contrato → runtime → editor;
- [arquitetura e execução](referencia-markdown/arquitetura-e-execucao.md);
- [worker e SQL Server](referencia-markdown/integracao-worker-banco.md);
- [migração e rollback](v2/migracao-e-rollback.md);
- [manual do cliente do Recorder](recorder/manual-cliente.md);
- [manual do desenvolvedor do Recorder](recorder/manual-desenvolvedor.md);
- [privacidade do Recorder](recorder/privacidade.md);
- [cobertura de interações do Recorder](recorder/cobertura-interacoes.md);
- [troubleshooting do Recorder](recorder/troubleshooting.md);
- [aceite em instalação limpa](recorder/teste-instalacao-limpa.md);
- [relatório do aceite humano REC-140](recorder/relatorio-instalacao-limpa.md);
- [evidências do gate REC-G12](recorder/release-gate.md);
- [solução de problemas](referencia-markdown/tutorial-solucao-problemas-rpa-blockly.md);
- [ADRs](adr/README.md).

`manual.html` é uma página inicial offline e aponta para estas fontes versionadas.
O schema 1 não é documentação operacional: suas fixtures ficam isoladas em
`tests/RpaFlow.MigratorChecks/Fixtures/baseline-v1`.
