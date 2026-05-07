# ADR-0003: Outbox transacional para eventos de domínio

- Status: Accepted
- Data: 2026-05-07
- Revisado: 2026-05-07 (substituiu storage e dispatcher custom pela
  feature `Paramore.Brighter.Outbox.PostgreSql` v9.9.13).

## Contexto

Aggregates de domínio emitem eventos (`BidPlacedEvent`,
`AuctionClosedEvent`, `AuctionCancelledEvent`) que subscribers
downstream se importam. Publicá-los inline a partir do command
handler criou dois modos de falha distintos:

1. **Crash entre commit e publish.** Estado persistido, evento
   silenciosamente perdido.
2. **Falha de publish após commit.** Mesmo resultado para o consumer,
   e o handler já não consegue rollback.

Ambos os casos violam o contrato de delivery at-least-once que os
consumers esperam.

## Decisão

Adotar o pattern outbox transacional usando a feature nativa do
**Brighter v9.9.13** (`Paramore.Brighter.Outbox.PostgreSql` +
`Paramore.Brighter.PostgreSql`):

1. Aggregates acumulam eventos de domínio na coleção `DomainEvents`.
2. `DomainEventsSaveChangesInterceptor` (EF Core) drena os eventos
   no `SavingChangesAsync`, mapeia cada um para uma `Message` do
   Brighter (`Header.Topic = FQN`, `Body = JSON`,
   `Header.Bag["clr_type"] = AssemblyQualifiedName`) e os escreve
   via `PostgreSqlOutbox.AddAsync(...)` *dentro da mesma transação*
   que atualiza o aggregate. Atomicidade vem do
   `EntityFrameworkPostgreSqlConnectionProvider`, que devolve a
   `NpgsqlConnection` + `NpgsqlTransaction` ativas do `DbContext`
   pelo contrato `IPostgreSqlConnectionProvider` /
   `IAmABoxTransactionConnectionProvider` que o Brighter já espera.
3. Um `BrighterOutboxDispatcherWorker` dedicado (`BackgroundService`)
   chama `PostgreSqlOutbox.OutstandingMessagesAsync(...)`, lê o
   `clr_type` do `Header.Bag`, deserializa o `Body` para o evento
   tipado original e chama
   `IAmACommandProcessor.PublishAsync<T>` do Brighter para
   dispatch in-process aos `RequestHandlerAsync<T>` registrados.
4. Em sucesso, chama `PostgreSqlOutbox.MarkDispatchedAsync(messageId)`.
   Em falha, a linha permanece outstanding — a próxima iteração do
   worker tenta de novo.

Brighter v9.9.13 é dono tanto da **storage** (tabela
`outbox_messages` com schema BIGSERIAL/UUID/HeaderBag/Body do
`PostgreSqlOutboxBulder.GetDDL`) quanto do **bus** in-process. O
único código nosso é a ponte EF→Brighter (`EntityFramework
PostgreSqlConnectionProvider`) e o worker — tudo compatível com a
estrutura de adaptadores do Brighter.

## Por que Brighter v9.9.13 e não v10.x

A v10.0.x — que tínhamos pinada antes — **não expõe** os helpers
`UseOutbox` / `UseOutboxSweeper` no `ServiceCollectionExtensions`.
Esses convenience APIs só existem no v9.x estável e voltaram no
v10.4+, mas v10.4+ depende de `Microsoft.Extensions.*` 10.x e
`Npgsql` 10.x — incompatível com nosso pin .NET 9 / EF Core 9.0.10 /
Npgsql 9.0.4. O downgrade para v9.9.13 entrega a feature pedida sem
arrastar o stack inteiro para .NET 10.

## Alternativas consideradas

- **Dispatcher in-process direto sem outbox.** Rejeitada — era o
  design original. Ver "Contexto".
- **Storage e dispatcher custom (versão anterior do ADR).**
  Tínhamos uma tabela `OutboxMessages` com `FailureCount` /
  `NextAttemptAtUtc` / partial index e um worker próprio com
  `FOR UPDATE SKIP LOCKED`. Funcionava, mas não era idiomático
  Brighter — duplicava conceitos que o `PostgreSqlOutbox` já
  resolve. A feature nativa simplifica o vocabulário do projeto e
  aproxima a base de uma migração futura para transporte externo
  (Kafka/RabbitMQ).
- **Bump cascateado para .NET 10 + Brighter 10.4.x.** Rejeitada
  — quebra compat com EF Core 9.0.10 e Npgsql 9.0.4 sem ganho
  arquitetural correspondente (a v9 já entrega o outbox).
- **Debezium / CDC.** Rejeitada nessa escala. Adiciona Kafka +
  connector Debezium à superfície operacional para benefícios que
  o sweeper de polling já entrega no nosso throughput.
- **Two-phase commit (XA).** Rejeitada. Não é suportado na nossa
  stack de broker e seria regressão operacional enorme.

## Consequências

- Toda réplica da API roda o worker dispatcher. O `PostgreSqlOutbox`
  do Brighter v9.9.13 **não usa `FOR UPDATE SKIP LOCKED`** — ele
  faz `SELECT ... WHERE Dispatched IS NULL`. Réplicas concorrentes
  podem ler a mesma mensagem antes do `MarkDispatchedAsync` commit.
  Para fechar essa janela sem precisar de handler idempotente em
  cada novo subscriber, o `BrighterOutboxDispatcherWorker`
  pré-reivindica cada `MessageId` na tabela `processed_events`
  (PRIMARY KEY em `event_id`) via
  `INSERT ... ON CONFLICT DO NOTHING` antes de chamar
  `PublishAsync<T>`. A réplica que não consegue inserir pula o
  publish — o handler roda **no máximo uma vez por evento**. A
  contagem de duplicates virou métrica:
  `zetauction_outbox_duplicate_skipped_total`.
- **Poison-message protection.** Brighter v9.9.13 não tem
  `FailureCount` no schema. O worker mantém um
  `ConcurrentDictionary<Guid, int>` em memória; depois de 10
  tentativas falhas, a mensagem é forçada para `Dispatched`
  (sai do polling) e o counter
  `zetauction_outbox_dead_lettered_total` incrementa — alvo de
  alerta SEV-2. A linha permanece em `outbox_messages` para
  triagem (ver runbook).
- **Defesa em profundidade na deserialização.** O `clr_type` do
  `Header.Bag` passa por `Type.GetType(...)` → o tipo resolvido é
  validado contra `IDomainEvent.IsAssignableFrom`. Se um atacante
  com write no banco injetar um tipo arbitrário, o dispatcher
  recusa.
- Schema da tabela é fixado pelo Brighter
  (`PostgreSqlOutboxBulder.GetDDL`), não pelo nosso EF model.
  A migration `20260507042221_AddBrighterOutbox` cria
  `outbox_messages` via `migrationBuilder.Sql(...)` e adiciona um
  índice parcial `WHERE Dispatched IS NULL` para manter o lookup
  do worker barato.
- O envelope `Message` do Brighter (Topic + MessageType +
  HeaderBag + Body) é mais rico do que o que tínhamos antes — abre
  caminho para tracing (header Bag carrega `aggregate_id`,
  `aggregate_type` e `clr_type`).
- Tipos de mensagem ficam armazenados como assembly-qualified
  no `Header.Bag["clr_type"]`. Renames / movimentações exigem
  migration deliberada (ver runbook).
- Pagamos uma escrita extra de linha por evento. Em throughput de
  produção isso fica muito abaixo do volume de lances, então é
  não-issue.
