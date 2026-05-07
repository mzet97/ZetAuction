# Sequência — colocar lance

Dois cenários: o happy path e o caminho contendido onde o token de
optimistic concurrency força um retry.

## Happy path

```mermaid
sequenceDiagram
    autonumber
    actor B as Bidder
    participant API as ZetAuction API
    participant RL as RedisRateLimiter
    participant H as PlaceBidCommandHandler
    participant UoW as UnitOfWork
    participant DB as Postgres
    participant IC as SaveChangesInterceptor
    participant OB as PostgreSqlOutbox
    participant CACHE as RedisCacheService
    participant W as BrighterOutboxDispatcherWorker

    B->>API: POST /api/v1/auctions/id/bids
    API->>RL: IsAllowedAsync(user, 5, 300)
    RL-->>API: true
    API->>H: PlaceBidCommand
    H->>UoW: BeginTransactionAsync
    H->>DB: SELECT FROM Auctions WHERE Id = ...
    DB-->>H: Auction com xmin=N
    H->>H: auction.PlaceBid(...) gera Bid e BidPlacedEvent
    H->>UoW: SaveChangesAsync
    UoW->>IC: SavingChangesAsync
    IC->>OB: AddAsync(Message BidPlacedEvent) na mesma transação EF
    UoW->>DB: UPDATE auction WHERE xmin = N
    DB-->>UoW: 1 row affected, xmin avança para N+1
    UoW-->>H: ok
    H->>UoW: CommitAsync
    H->>CACHE: HSET highest_bid via Lua CAS
    H-->>API: BaseResult.Ok
    API-->>B: 200 OK
    Note over W,OB: De forma assíncrona
    W->>OB: OutstandingMessagesAsync()
    OB-->>W: Message BidPlacedEvent
    W->>W: resolve clr_type e deserializa Body
    W->>W: PublishAsync para BidPlacedEvent handler chain
    W->>OB: MarkDispatchedAsync(messageId)
```

## Caminho contendido (conflito de xmin, retry Polly)

```mermaid
sequenceDiagram
    autonumber
    actor B as Bidder B
    actor C as Bidder C
    participant API as ZetAuction API
    participant H as PlaceBidCommandHandler
    participant DB as Postgres
    participant Polly as ResiliencePipeline

    par Dois placements concorrentes no mesmo xmin
        B->>API: POST bids amount=110
    and
        C->>API: POST bids amount=120
    end
    API->>H: command de B
    API->>H: command de C
    H->>DB: SELECT auction (B) — xmin=N
    H->>DB: SELECT auction (C) — xmin=N
    Note over H,DB: Ambos handlers veem xmin=N
    H->>DB: UPDATE WHERE xmin=N (B commita primeiro)
    DB-->>H: 1 row, xmin=N+1
    H->>DB: UPDATE WHERE xmin=N (C)
    DB-->>H: 0 rows, DbUpdateConcurrencyException
    H->>Polly: rethrow ConcurrencyConflictException
    Polly->>Polly: backoff 20ms com jitter
    Polly->>H: retry
    H->>DB: SELECT auction — xmin=N+1, vê o lance de B
    H->>H: PlaceBid(120) em cima de 110, ainda válido
    H->>DB: UPDATE WHERE xmin=N+1
    DB-->>H: 1 row, xmin=N+2
    H-->>API: ok
    API-->>C: 200 OK
```

## Notas

- O passo "B commita primeiro" é resolvido por contenção pelo MVCC
  do Postgres. As duas transações estão em vôo; quem terminar o
  UPDATE primeiro ganha.
- O retry no Polly reseta o change tracker do EF Core
  (`ResetTrackingAsync`), então a segunda tentativa relê o leilão
  com o novo xmin.
- Se o pipeline de retry queima as três tentativas, o
  `409 ConcurrencyConflict` original (RFC 7807) volta para o cliente
  — não um `500`.
- O counter `zetauction.db.concurrency_conflicts` incrementa em todo
  retry, dando ao Grafana visibilidade sobre contenção.
