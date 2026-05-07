# Sequência — dispatch do outbox

Drena o outbox transacional do Brighter (`PostgreSqlOutbox` v9.9.13)
para o `IAmACommandProcessor` in-process. Roda em toda réplica
da API. Como `OutstandingMessagesAsync` é um SELECT simples (não
`FOR UPDATE SKIP LOCKED`), réplicas concorrentes podem ler a mesma
linha — para fechar essa janela, o worker pré-reivindica o
`MessageId` na tabela `processed_events` antes de publicar. Apenas
a réplica que ganha o INSERT roda o handler chain (ver ADR-0003).

## Polling loop

```mermaid
sequenceDiagram
    autonumber
    participant W as BrighterOutboxDispatcherWorker
    participant Ob as PostgreSqlOutbox
    participant DB as Postgres

    loop a cada 500ms
        W->>Ob: OutstandingMessagesAsync (pageSize=50)
        Ob->>DB: SELECT FROM outbox_messages WHERE Dispatched IS NULL LIMIT 50
        DB-->>Ob: rows
        Ob-->>W: Message list
        Note over W: para cada Message, executa o fluxo abaixo
    end
```

## Por mensagem — caminho de sucesso (réplica vence o claim)

```mermaid
sequenceDiagram
    autonumber
    participant W as BrighterOutboxDispatcherWorker
    participant Ob as PostgreSqlOutbox
    participant DB as Postgres
    participant CP as IAmACommandProcessor
    participant H as Handler

    W->>W: clrType = Header.Bag clr_type
    W->>W: Type.GetType(clrType)
    W->>W: validate IDomainEvent.IsAssignableFrom
    W->>W: JsonSerializer.Deserialize(Body)
    W->>DB: INSERT INTO processed_events ON CONFLICT DO NOTHING
    DB-->>W: rows affected = 1
    W->>CP: PublishAsync(domainEvent)
    CP->>H: HandleAsync(domainEvent)
    H-->>CP: ok
    CP-->>W: ok
    W->>Ob: MarkDispatchedAsync(messageId)
    Ob->>DB: UPDATE outbox_messages SET Dispatched = now()
```

## Por mensagem — réplica perde o claim (idempotência multi-réplica)

```mermaid
sequenceDiagram
    autonumber
    participant W as BrighterOutboxDispatcherWorker
    participant Ob as PostgreSqlOutbox
    participant DB as Postgres

    W->>DB: INSERT INTO processed_events ON CONFLICT DO NOTHING
    DB-->>W: rows affected = 0
    W->>W: increment zetauction_outbox_duplicate_skipped
    Note over W: pula PublishAsync (outra réplica já entregou)
    W->>Ob: MarkDispatchedAsync(messageId)
    Ob->>DB: UPDATE outbox_messages SET Dispatched = now()
```

## Por mensagem — falha transiente (retry na próxima iteração)

```mermaid
sequenceDiagram
    autonumber
    participant W as BrighterOutboxDispatcherWorker
    participant Ob as PostgreSqlOutbox
    participant DB as Postgres
    participant CP as IAmACommandProcessor

    W->>CP: PublishAsync(domainEvent)
    CP-->>W: throws (handler bug, JSON inválido, etc.)
    W->>W: failureCount++
    W->>W: increment zetauction_outbox_failed
    W->>W: log warning attempt N of 10
    Note over W,DB: linha permanece Dispatched=NULL — repolada
```

## Por mensagem — poison message (dead-letter após 10 tentativas)

```mermaid
sequenceDiagram
    autonumber
    participant W as BrighterOutboxDispatcherWorker
    participant Ob as PostgreSqlOutbox
    participant DB as Postgres
    participant CP as IAmACommandProcessor

    W->>CP: PublishAsync(domainEvent)
    CP-->>W: throws (10ª vez consecutiva)
    W->>W: failureCount = 10
    W->>W: log error exceeded 10 dispatch attempts
    W->>W: increment zetauction_outbox_dead_lettered
    W->>Ob: MarkDispatchedAsync(messageId)
    Ob->>DB: UPDATE outbox_messages SET Dispatched = now()
    Note over W,DB: linha sai do polling, payload preservado para triagem
```

## Propriedades

- **Delivery at-least-once.** O `PostgreSqlOutbox` só marca a linha
  como dispatched depois do `PublishAsync` retornar com sucesso. Um
  crash entre `PublishAsync` e `MarkDispatchedAsync` deixa a linha
  outstanding — a próxima iteração re-tenta o pre-claim, encontra a
  linha em `processed_events` (0 rows), pula o publish (não duplica)
  e marca dispatched.
- **Handler at-most-once por evento.** A pré-reivindicação em
  `processed_events` (PRIMARY KEY em `event_id`,
  `INSERT ... ON CONFLICT DO NOTHING`) garante que apenas uma
  réplica entra no chain do handler para um dado `MessageId`, mesmo
  quando múltiplas réplicas selecionam a linha em paralelo. Novos
  subscribers não precisam implementar dedup própria.
- **Defesa em profundidade na deserialização.** O `clr_type` é
  primeiro resolvido por `Type.GetType(...)` e depois validado
  contra `IDomainEvent.IsAssignableFrom`. Tipo arbitrário injetado
  via write no banco é recusado pelo dispatcher antes do handler.
- **Poison-message dead-letter.** O worker mantém um
  `ConcurrentDictionary<Guid, int>` em memória. Após 10 tentativas
  falhas para a mesma `MessageId`, a linha é forçada para
  `Dispatched = now()` e a métrica
  `zetauction_outbox_dead_lettered_total` incrementa (alvo de
  alerta SEV-2). A linha **permanece** em `outbox_messages` com
  `Dispatched` populado para triagem.
- **Resiliência a deployment skew.** Um `clr_type` ausente faz o
  dispatch falhar **transiente** até a tentativa 10. Se o assembly
  certo entrar em produção dentro dessa janela, a mensagem drena
  normalmente; caso contrário, dead-letter.

## Métricas relacionadas

| Métrica | Significado |
| --- | --- |
| `zetauction_outbox_dispatched_total` | Mensagens drenadas com sucesso |
| `zetauction_outbox_failed_total` | Tentativas de dispatch que falharam (não inclui dead-letter) |
| `zetauction_outbox_duplicate_skipped_total` | Réplica perdeu o claim em `processed_events` |
| `zetauction_outbox_dead_lettered_total` | Linhas forçadas para dispatched após 10 falhas (poison) |

## Runbook de falha

Ver `docs/runbooks/outbox-stuck-messages.md` para passos de triage
quando o counter de falhas ou de dead-letter sobe.
