# C3 — Visão de componente (container API)

Dá um zoom dentro do container da API ZetAuction. Cada caixa é um
componente lógico; setas são chamadas de método in-process ou
dependências resolvidas via DI.

```mermaid
flowchart TB
  classDef edge fill:#eef,stroke:#446,color:#000;
  classDef domain fill:#fef,stroke:#963,color:#000;
  classDef infra fill:#efe,stroke:#363,color:#000;
  classDef worker fill:#ffe,stroke:#963,color:#000;
  classDef cross fill:#fee,stroke:#933,color:#000;

  client[Cliente]:::edge

  subgraph api[Processo da API]
    direction TB

    subgraph edge[Edge / cross-cutting]
      mw1[CorrelationIdMiddleware]:::cross
      mw2[SecurityHeadersMiddleware]:::cross
      mw3[ProblemDetailsExceptionHandler]:::cross
      mw4[RateLimiter — auth policy]:::cross
      mw5[JwtBearer auth]:::cross
    end

    subgraph endpoints[Endpoints Minimal API]
      e1[AuthEndpoints]:::edge
      e2[AuctionEndpoints]:::edge
      e3[BidEndpoints]:::edge
      e4[UserEndpoints]:::edge
      e5[Healthchecks]:::edge
    end

    subgraph dispatch[Dispatch CQRS]
      brighter[Brighter — IAmACommandProcessor]:::infra
      darker[Darker — IQueryProcessor]:::infra
    end

    subgraph application[Camada Application]
      cmd[Command handlers — RequestHandlerAsync]:::domain
      qry[Query handlers — QueryHandlerAsync]:::domain
      polly[ResiliencePipeline — retry em ConcurrencyConflict]:::cross
    end

    subgraph domain[Camada Domain]
      auc[Auction aggregate]:::domain
      bid[Bid entity]:::domain
      user[User aggregate]:::domain
      ev[Eventos de domínio]:::domain
      ex[Exceções de domínio]:::domain
    end

    subgraph infra[Camada Infrastructure]
      uow[UnitOfWork — EF Core]:::infra
      repos[Repositórios]:::infra
      ic[DomainEventsSaveChangesInterceptor]:::infra
      cache[RedisCacheService — Lua CAS]:::infra
      rl[RedisRateLimiterService — Polly CB]:::infra
      jwt[JwtService — HS256]:::infra
      ph[PasswordHasher — BCrypt]:::infra
    end

    subgraph workers[Background workers]
      fin[AuctionFinalizationWorker]:::worker
      out[BrighterOutboxDispatcherWorker]:::worker
    end

    subgraph obs[Observabilidade]
      met[ZetAuctionMetrics — meter OTel]:::infra
      log[Serilog + sink OTLP]:::infra
    end
  end

  pg[(Postgres 16)]
  redis[(Redis 7)]
  otel((OTel Collector))

  client --> mw1 --> mw2 --> mw3 --> mw4 --> mw5 --> endpoints
  endpoints --> brighter
  endpoints --> darker
  endpoints --> jwt
  brighter --> cmd
  darker --> qry
  cmd --> polly --> uow
  cmd --> auc
  cmd --> cache
  cmd --> rl
  qry --> repos
  qry --> cache
  uow --> repos
  uow --> ic
  ic --> repos
  repos --> pg
  cache --> redis
  rl --> redis
  fin --> uow
  out --> brighter
  out --> repos
  cmd --> met
  qry --> met
  workers --> met
  obs --> otel
```

## Responsabilidades dos componentes

### Edge / cross-cutting

| Componente | Função |
| --- | --- |
| `CorrelationIdMiddleware` | Lê `X-Correlation-Id`, cai para o `traceparent` W3C, depois para um GUID novo. Empurra para o `LogContext` do Serilog |
| `SecurityHeadersMiddleware` | HSTS, CSP, framing/referrer/permissions policies em toda resposta |
| `ProblemDetailsExceptionHandler` | Mapeia exceptions de domínio para respostas RFC 7807 |
| `RateLimiter` | Fixed-window 5/5min por IP em `/api/v1/auth/*` |
| `JwtBearer` | Validação HS256 com `Jwt:SecretKey`; `ValidAlgorithms` fixo em HmacSha256 |

### Camada Application

Command handlers embrulham uma transação do UoW, participam do
pipeline Polly de retry (`PlaceBidCommandHandler`) e nunca publicam
domain events direto — o interceptor faz isso (ADR-0003).

Query handlers leem pelo caminho cache-first
(`GetHighestBidQueryHandler`) e emitem métricas de hit/miss.

### Camada Infrastructure

`UnitOfWork` traduz `DbUpdateConcurrencyException` para
`ConcurrencyConflictException`. O
`DomainEventsSaveChangesInterceptor` drena os eventos do aggregate,
mapeia cada um para uma `Paramore.Brighter.Message` (Topic = FQN,
Body = JSON, `Header.Bag["clr_type"]` = AssemblyQualifiedName) e
grava via `PostgreSqlOutbox.AddAsync` na mesma transação EF, usando
`EntityFrameworkPostgreSqlConnectionProvider` como bridge.

`RedisCacheService` roda um script Lua que só faz HSET quando o
amount recebido excede estritamente o valor armazenado (ADR-0004).

`RedisRateLimiterService` embrulha a chamada Redis em um circuit
breaker Polly que cai para um limiter in-memory em falha do Redis.

### Background workers

`AuctionFinalizationWorker` clama leilões vencidos sob
`FOR UPDATE SKIP LOCKED`, avança eles para `Finalized` e deixa o
interceptor escrever `AuctionClosedEvent` no outbox.

`BrighterOutboxDispatcherWorker` faz polling do `PostgreSqlOutbox`
via `OutstandingMessagesAsync`, deserializa o payload de volta para
o evento tipado (resolvendo via `Header.Bag["clr_type"]` e validando
contra `IDomainEvent.IsAssignableFrom`) e publica via
`IAmACommandProcessor.PublishAsync<T>`. Brighter v9.9.13 não usa
`FOR UPDATE SKIP LOCKED` nas linhas — para fechar a janela
multi-réplica, o worker pré-reivindica o `MessageId` na tabela
`processed_events` (`INSERT … ON CONFLICT DO NOTHING`) **antes** do
publish. A réplica que perde o claim pula o handler chain e ainda
chama `MarkDispatchedAsync`. Mensagens permanentemente falhas são
dead-lettered (forçadas para `Dispatched`) após 10 tentativas, com
métrica `zetauction.outbox.dead_lettered` para alerta (ADR-0003).
