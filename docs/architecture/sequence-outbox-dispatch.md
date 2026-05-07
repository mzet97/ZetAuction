# Sequência — dispatch do outbox

Drena o outbox transacional do Brighter (`PostgreSqlOutbox` v9.9.13)
para o `IAmACommandProcessor` in-process. Roda em toda réplica
da API. Réplicas concorrentes podem ler a mesma linha antes do
`MarkDispatchedAsync` commit (`OutstandingMessagesAsync` é um
SELECT simples, não `FOR UPDATE SKIP LOCKED`) — handlers são
idempotentes por contrato (ver ADR-0003).

```mermaid
sequenceDiagram
    autonumber
    participant W as BrighterOutboxDispatcherWorker
    participant Ob as PostgreSqlOutbox
    participant DB as Postgres (outbox_messages)
    participant CP as IAmACommandProcessor
    participant H as RequestHandlerAsync<T>

    loop a cada 500ms (ou após cada batch vazio)
        W->>Ob: OutstandingMessagesAsync(0ms, pageSize=50, page=1)
        Ob->>DB: SELECT * FROM outbox_messages WHERE Dispatched IS NULL ORDER BY Timestamp LIMIT 50
        alt batch vazio
            DB-->>Ob: []
            Ob-->>W: []
        else batch tem mensagens
            DB-->>Ob: Message[...]
            Ob-->>W: Message[...]
            loop por mensagem
                W->>W: clrType = Header.Bag["clr_type"]
                W->>W: Type.GetType(clrType, throw=false)
                alt tipo resolve
                    W->>W: JsonSerializer.Deserialize(Body.Value, eventType)
                    W->>CP: PublishAsync<T>(domainEvent)
                    CP->>H: HandleAsync(domainEvent)
                    H-->>CP: ok
                    CP-->>W: ok
                    W->>Ob: MarkDispatchedAsync(messageId, now)
                    Ob->>DB: UPDATE outbox_messages SET Dispatched = now() WHERE MessageId = ...
                else dispatch falhou (qualquer razão)
                    W->>W: log warning, increment zetauction_outbox_failed
                    Note over W,DB: Linha permanece Dispatched=NULL — repolada na próxima iteração.
                end
            end
        end
    end
```

## Propriedades

- **Delivery at-least-once.** O `PostgreSqlOutbox` só marca a linha
  como dispatched depois do `PublishAsync` retornar com sucesso. Um
  crash entre `PublishAsync` e `MarkDispatchedAsync` deixa a linha
  outstanding — a próxima iteração pode redispatchar (handlers
  idempotentes absorvem o duplicate).
- **Idempotência por contrato.** Brighter v9.9.13 não tem
  `FOR UPDATE SKIP LOCKED` no outbox. Réplicas concorrentes podem
  ler a mesma linha. `BidPlacedEventHandler`,
  `AuctionClosedEventHandler` e `AuctionCancelledEventHandler` são
  side-effect-free (logging + reads). Novos handlers precisam manter
  a propriedade.
- **Resiliência de tipo.** Um `clr_type` ausente (skew de deployment)
  faz o dispatch falhar mas não abandona a linha — assim que o
  handler correspondente entra em produção, a mensagem drena.

## Runbook de falha

Ver `docs/runbooks/outbox-stuck-messages.md` para passos de triage
quando o counter de falhas sobe mais rápido que o de dispatched.
