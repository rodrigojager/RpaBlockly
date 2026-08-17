# ADR-001 — Fronteira V1/V2

## Estado

Aceita.

## Contexto

O schema 1 mistura roteiro e seletores. Manter leitura implícita desse formato no
runtime V2 criaria duas semânticas para o mesmo executor, dificultaria rejeitar
propriedades antigas e prolongaria indefinidamente o código de compatibilidade.

## Decisão

O caminho operacional aceita somente `flow.schemaVersion = 2`. DTOs, loader e
executor V1 ficam em assemblies históricos usados apenas pelo migrador offline e
pelos testes diferenciais. Produção não referencia esses assemblies.

## Alternativas recusadas

- detectar a versão no loader operacional: mantém dois contratos em produção;
- converter V1 em memória no worker: esconde custo, warnings e revisão humana;
- sobrescrever o arquivo V1: elimina a origem necessária para auditoria e rollback.

## Consequências

- migração é uma etapa explícita antes da publicação;
- exemplos, template, editor e worker usam somente pacotes V2;
- incompatibilidades futuras exigem outro schema, não novos campos condicionais.

## Rollback

Restaurar o release anterior e a cópia V1 original. Não existe conversão reversa
automática, pois ela poderia perder decisões feitas no catálogo de localizadores.

## Testes e evidências

`RpaBase.Checks` prova por reflexão que assemblies operacionais não expõem DTO ou
executor V1 nem referenciam assemblies históricos. `RpaFlow.MigratorChecks`
valida todas as fixtures V1, e o loader V2 rejeita schema diferente de 2.
