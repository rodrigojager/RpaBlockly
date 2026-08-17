# Arquitetura e execução V2

## Camadas

| Camada | Responsabilidade |
| --- | --- |
| Contracts | DTOs schema 2, catálogo e validação local. |
| Packages | validação cruzada, snapshot, hash, revisão, stores e registry. |
| Runtime | request, contexto de dados, observer, falha e orçamento. |
| Playwright | compilador de receitas, resolver, handlers, heurística e artefatos. |
| Editor | autoria Blockly e publicação atômica. |
| Worker | claim SQL, configuração, snapshot, execução e persistência. |

O runtime V1 não faz parte dessas camadas operacionais. Ele existe em assemblies
históricos usados pelo migrador/check diferencial.

## Snapshot

Antes da primeira ação, o host resolve a revisão e cria um
`RpaPackageSnapshot`. A instância fornece cópias defensivas; executor e handlers
não alteram seu conteúdo. Publicações posteriores só afetam novas execuções.

## Resolver

Toda referência web passa por `LocatorResolver`. O compilador aplica frames,
scope e target; resolve filtros literal/source pelo `FlowDataContext`; verifica
cardinalidade/estado; e registra cada tentativa.

O orçamento pertence à resolução inteira, não a cada candidato. Página encerrada,
receita inválida e outros erros não recuperáveis interrompem imediatamente.

No modo adaptive, a coleta DOM é limitada e sanitizada. O ranking C# é
determinístico e usa o golden Scrapling somente como referência de calibração;
Python não é dependência de produção.

## Aprendizado

Uma sessão provisória pertence a um `executionId`. O candidato heurístico pode ser
reutilizado pela mesma execução, mas nenhuma outra o observa. Após `Succeeded`, a
policy pode promover em memória, source ou overlay. Persistência usa CAS; conflito
é evento explícito e não bloqueia os outros casos.

## Observabilidade e falha

Eventos registram execução, ação, RPA, locator, candidato, revisão, hash, tempo e
motivo sem valores sensíveis. Uma falha pode gerar screenshot mascarada, HTML
sanitizado/truncado e `resolucao.json`. Os artefatos respeitam limites e retenção.

## Extensibilidade

Novos canais podem reutilizar Contracts/Packages/Runtime. Um handler web novo deve
seguir [Como adicionar um bloco](como-adicionar-bloco.md). Regras particulares de
um RPA permanecem no host/aplicação local e não entram no catálogo genérico.
