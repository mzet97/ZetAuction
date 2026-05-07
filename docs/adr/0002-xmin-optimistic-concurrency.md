# ADR-0002: `xmin` do Postgres para optimistic concurrency

- Status: Accepted
- Data: 2026-05-07

## Contexto

Múltiplas réplicas da API colocam lances na mesma linha de leilão.
Precisamos de um modelo de concorrência que:

- Deixe o banco — não a aplicação — ser a fonte da verdade para "alguém
  atualizou essa linha desde que eu li?".
- Seja barato no caminho de leitura (sem coluna extra para ler, sem
  cache compartilhado).
- Seja óbvio quando falha (queremos surfacear
  `DbUpdateConcurrencyException`, fazer retry e reportar).

## Decisão

Mapear a system column `xmin` do Postgres (id da transação que mexeu
na linha por último) como uma propriedade `uint RowVersion` com
`IsConcurrencyToken = true` e `ValueGeneratedOnAddOrUpdate`. O EF Core
vai anexar `WHERE xmin = @oldXmin` em todo UPDATE; se zero linhas
casarem, o EF Core lança `DbUpdateConcurrencyException`, que o
`UnitOfWork` traduz para nossa `ConcurrencyConflictException` e o
pipeline Polly em `PlaceBidCommandHandler` retenta até três vezes com
backoff exponencial e jitter.

## Alternativas consideradas

- **Adicionar coluna `Version` manual com triggers de linha.**
  Rejeitada. Dobra a superfície de escrita, exige um trigger pra
  manter, e não nos dá nada que `xmin` já não dê.
- **`SELECT FOR UPDATE` na linha do leilão.** Rejeitada para o hot
  path do lance — serializa os bidders e cria um cliff de tail
  latency exatamente quando o sistema está sob carga. Aceitável para o
  worker que finaliza leilão (ver ADR-0003).
- **Lock em nível de aplicação via Redis (`SET NX EX`).** Rejeitada.
  Acopla correção à disponibilidade do Redis e adiciona um round trip
  por escrita. O token `xmin` é local à transação que já estamos
  pagando.

## Consequências

- `Auction.RowVersion` faz parte do estado persistido e precisa ser
  serializado pelo interceptor do outbox (e é — ver
  `DomainEventsSaveChangesInterceptor`).
- O pipeline de retry absorve conflitos benignos e surfacing os
  duros como `409 Conflict` em respostas ProblemDetails (ver
  ADR-0009).
- Qualquer write não-EF (SQL puro) PRECISA setar `xmin = OLD.xmin` para
  manter o contrato. Hoje só o worker de finalização escreve via SQL
  puro, e ele clama linhas sob `FOR UPDATE SKIP LOCKED`, então o
  contrato do xmin continua seguro.
