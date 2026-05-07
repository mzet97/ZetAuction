# ADR-0005: Pipeline Polly para retry de optimistic concurrency

- Status: Accepted
- Data: 2026-05-07

## Contexto

`xmin` (ADR-0002) nos dá optimistic concurrency de graça. A
consequência direta é que uma linha contendida produz
`ConcurrencyConflictException` benignos — o primeiro writer a
commitar "ganha" e todos os outros perdem a tentativa. Sem retry,
todo lance perdedor sobe como `409` para o cliente, mesmo que o
bidder estivesse disposto a tentar de novo no mesmo valor.

## Decisão

Embrulhar o caminho de placement de lance num
`ResiliencePipeline` Polly v8:

- `MaxRetryAttempts = 3`
- `BackoffType = Exponential` com `Delay = 20ms` e jitter
- `ShouldHandle` apenas `ConcurrencyConflictException`

Antes de cada retry, o pipeline chama
`IUnitOfWork.ResetTrackingAsync` para o change tracker do EF Core
recarregar o leilão com o novo xmin. O retry é invisível ao cliente,
a menos que todas as três tentativas percam a corrida — caso em que
o `409` original é retornado.

## Alternativas consideradas

- **Pessimistic locking na linha do leilão.** Rejeitada — ver
  ADR-0002.
- **Fila por leilão em nível de aplicação.** Rejeitada. Conserta o
  sintoma, cria um novo gargalo.
- **Mais retries.** Rejeitada. Retentar além de três tentativas
  raramente sucede (a contenção não vai sumir em 100ms) e amplifica
  tail latency para bidders cujo lance falharia em regras de negócio
  de qualquer forma.

## Consequências

- `zetauction.db.concurrency_conflicts` (métrica da Phase 6)
  incrementa em todo retry, dando visibilidade sobre contenção. O
  dashboard do Grafana surfaca isso como um stat panel.
- Retries são limitados — sem risco de tail latency ilimitada.
- Testes que dirigem lances concorrentes precisam permitir o pipeline
  fazer múltiplas tentativas antes de assertar o resultado. Os chaos
  tests de integração fazem isso corretamente (ver
  `ConcurrencyChaosTests.cs`).
