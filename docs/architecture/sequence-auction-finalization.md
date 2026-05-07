# Sequência — finalização do leilão

O worker de finalizer roda em toda réplica da API. Ele clama
leilões vencidos sob `FOR UPDATE SKIP LOCKED`, então múltiplas
réplicas nunca tentam finalizar a mesma linha.

```mermaid
sequenceDiagram
    autonumber
    participant W1 as Worker (réplica 1)
    participant W2 as Worker (réplica 2)
    participant DB as Postgres
    participant IC as SaveChangesInterceptor
    participant OB as outbox_messages (PostgreSqlOutbox)
    participant OW as BrighterOutboxDispatcherWorker

    par Ambas réplicas acordam no mesmo tick
        W1->>DB: BEGIN
        W2->>DB: BEGIN
    end
    W1->>DB: SELECT … FROM auctions WHERE Status='Active' AND EndDate <= now() FOR UPDATE SKIP LOCKED LIMIT 100
    DB-->>W1: linhas {A,B,C} locked por W1
    W2->>DB: SELECT … FROM auctions WHERE Status='Active' AND EndDate <= now() FOR UPDATE SKIP LOCKED LIMIT 100
    DB-->>W2: linhas {D,E} (puladas as de W1)
    par Fan-out independente
        W1->>W1: Para cada: auction.Close(now)
        W2->>W2: Para cada: auction.Close(now)
    end
    W1->>DB: SaveChangesAsync (UPDATE auctions SET Status='Finalized'...)
    W2->>DB: SaveChangesAsync (UPDATE auctions SET Status='Finalized'...)
    IC->>OB: PostgreSqlOutbox.AddAsync(Message{AuctionClosedEvent}) — same EF transaction
    W1->>DB: COMMIT
    W2->>DB: COMMIT

    Note over OW,OB: De forma assíncrona
    OW->>OB: OutstandingMessagesAsync()
    OW->>OW: IAmACommandProcessor.PublishAsync<AuctionClosedEvent>(...)
    OW->>OB: MarkDispatchedAsync(messageId)
```

## Por que `FOR UPDATE SKIP LOCKED`

Um `SELECT FOR UPDATE` ingênuo enfileira ambos os workers atrás do
mesmo rowset, batendo no propósito de rodar o worker em toda
réplica. O modificador `SKIP LOCKED` pede ao Postgres para passar
por linhas já locked por outra transação, então os workers
paralelizam naturalmente sem estado de coordenação no Redis ou em
memória.

## Modos de falha

- Se um worker crasha entre `SELECT FOR UPDATE` e `COMMIT`, o
  Postgres libera os locks no abort da transação. A próxima
  iteração do worker pega as linhas de volta.
- Se um worker crasha entre `COMMIT` e o dispatcher do outbox pegar
  as linhas novas, a tabela `outbox_messages` é durável (gerenciada
  pelo `Paramore.Brighter.Outbox.PostgreSql`), então os eventos
  ainda saem eventualmente.
- O intervalo é intencionalmente curto (5s quando idle) — o backlog
  é limitado pelo tempo entre `EndDate` e o próximo tick do worker.
