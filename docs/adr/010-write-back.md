# ADR-010 — Write-back

## Estado

Aceita.

## Contexto

Nem toda origem pode ou deve ser alterada. Testes precisam de aprendizado efêmero;
arquivo e SQL podem aceitar nova revisão; pacote inline deve permanecer somente
leitura; overlays permitem separar autoria e aprendizado.

## Decisão

Os modos são `Disabled`, `Memory`, `Source` e `Overlay`. O provider declara suas
capacidades e combinações impossíveis falham na validação do pacote antes da
execução. Confirmação usa compare-and-swap.

## Alternativas recusadas

- sempre gravar na origem: quebra fontes somente leitura;
- fallback silencioso entre modos: muda durabilidade sem consentimento;
- cache TTL como write-back: não cria revisão auditável.

## Consequências

`Source` atualiza a origem revisionada; `Overlay` publica camada separada;
`Memory` termina com o processo; `Disabled` nunca persiste. Conflito não sobrescreve.

## Rollback

Alterar a política para `Disabled` interrompe novas gravações. Revisões já criadas
continuam no histórico e podem deixar de ser atuais por publicação via CAS.

## Testes e evidências

`RpaFlow.PlaywrightChecks` cobre sucesso e descarte por modo; packages e SQL cobrem
conflito de revisão e fontes somente leitura.
