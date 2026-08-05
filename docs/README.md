# Documentação

- `manual.html`: guia principal e catálogo interativo completo.
- `manual.config.js`: título, organização, cores, tema e texto de suporte.
- `assets/block-catalog.js`: fonte configurável das 35 seções de blocos.
- `assets/manual.css`: aparência responsiva e modo de impressão.
- `assets/manual.js`: renderização, pesquisa, filtros, tema e cópia de exemplos.
- `referencia-markdown/`: documentação técnica complementar do runtime e editor.

Abra o manual diretamente ou execute `..\abrir-manual.cmd`. Nenhum arquivo depende de internet.

Ao adicionar ou alterar um bloco, atualize `block-catalog.js` na mesma mudança e execute `tests/RpaBase.Checks`; o teste compara os tipos presentes no editor com os tipos documentados.
