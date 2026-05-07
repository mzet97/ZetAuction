# Changelog

Todas as mudanças notáveis nesse projeto estão documentadas neste
arquivo.

O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/),
e este projeto adere a [Semantic Versioning](https://semver.org/lang/pt-BR/spec/v2.0.0.html).

## [Unreleased]

### Adicionado
- Outbox transacional agora é o nativo do
  `Paramore.Brighter.Outbox.PostgreSql` v9.9.13: schema
  `outbox_messages` (BIGSERIAL/UUID/HeaderBag/Body) gerado pela
  migration `AddBrighterOutbox` e adapter
  `EntityFrameworkPostgreSqlConnectionProvider` que faz bridge da
  transação do `DbContext` para o `PostgreSqlOutbox.AddAsync` no
  `SavingChangesAsync`. Substitui a tabela e dispatcher custom
  anteriores. Ver ADR-0003 (revisado 2026-05-07).
- `BrighterOutboxDispatcherWorker`: `BackgroundService` que drena
  `OutstandingMessagesAsync`, deserializa via `clr_type` do
  `Header.Bag` e republica via `IAmACommandProcessor.PublishAsync<T>`,
  marcando com `MarkDispatchedAsync` em sucesso. Inclui:
  pré-reivindicação cross-réplica via tabela `processed_events`
  (INSERT ON CONFLICT DO NOTHING) para "handler at-most-once
  per event"; whitelist `IDomainEvent.IsAssignableFrom` na
  resolução do `clr_type`; cache de `MethodInfo` da reflection;
  e dead-letter automático após 10 tentativas
  (`zetauction_outbox_dead_lettered_total`).
- Tabela `processed_events` (`event_id UUID PK, processed_at
  timestamptz NOT NULL`) via migration `AddProcessedEvents`.
- Métricas custom novas: `zetauction.outbox.duplicate_skipped`
  (réplica perdeu claim) e `zetauction.outbox.dead_lettered`
  (poison-message; alvo de alerta).
- `IUnitOfWork.BeginTransactionAsync` / `RollbackAsync` agora
  aceitam `CancellationToken` — sob saturação de pool a chamada
  pode ser cancelada.
- Baseline de governança: `CONTRIBUTING.md`, `CODEOWNERS`, `SECURITY.md`, `CHANGELOG.md`.
- Layout do projeto: `docs/adr`, `docs/architecture`, `docs/runbooks`, `docs/security`, `docs/api-examples`, `scripts/`, `infra/`, `tests/load`.
- Token de optimistic concurrency (`Auction.RowVersion` mapeado para
  o `xmin` do Postgres) para que placements concorrentes de lance
  vindos de múltiplas réplicas da API sejam rejeitados pelo banco
  com `DbUpdateConcurrencyException` em vez de sobrescrever
  silenciosamente.
- Migration EF Core inicial `InitialCreate` em
  `src/ZetAuction.Infrastructure/Persistence/Migrations/`.
- Índice `IX_Auctions_Status_EndDate` para acelerar o scan do worker
  de finalização introduzido na Phase 3.
- Testes de integração agora sobem um container ephemeral do
  Postgres 16 via Testcontainers e resetam estado entre testes via
  Respawn.
- Job `api-migrate` no `docker-compose.yml` que aplica migrations
  uma vez antes das réplicas da API começarem.
- Pipeline OpenTelemetry completo (traces + métricas + logs) com
  exporter OTLP gRPC para o Collector; stack docker-compose com
  Jaeger, Prometheus, Loki e Grafana provisionados.
- `JwtService` HS256 com validação de comprimento mínimo do segredo
  (`Jwt:SecretKey` >= 32 caracteres no startup) e `ValidAlgorithms`
  fixado para fechar a porta a algorithm confusion.
- `PasswordHasher` BCrypt para hash de senhas.

### Mudado
- Movidos `ARQUITETURA.md` e `SDD.md` para sob `docs/architecture/`.
- Movida a especificação do assessment para `docs/assessment.md`
  (fonte única).
- Substituído SQLite por Postgres 16 como engine de persistência
  canônico (Npgsql.EntityFrameworkCore.PostgreSQL). SQLite não é
  mais suportado.
- Substituído o bootstrap de startup `EnsureCreatedAsync` por
  `MigrateAsync` para que mudanças de schema sejam versionadas e
  reproduzíveis entre ambientes.
- `CreateAuctionCommand` e `AuctionViewModel` agora expõem só os
  nomes de campo exigidos pelo spec do assessment (`name`,
  `startingBid`, `endDateTime`); o aggregate de domínio mantém o
  vocabulário canônico (`Title`, `StartingPrice`, `EndDate`) e o
  handler ponteia entre os dois.
- Health check agora prova Postgres (`AspNetCore.HealthChecks.NpgSql`).
- Exceptions de domínio para falhas client-side (lance insuficiente,
  etc.) agora são logadas em nível warning em vez de error.
- Auth simplificada para JWT HS256 + BCrypt conforme orientado no
  enunciado do assessment. RS256/JWKS + Argon2id ficam registrados no
  README como evolução planejada (quando a validação for delegada a
  outro resource server e o atacante de senha for GPU-bound).
- `AuctionRepository.LockDueForFinalizationAsync` agora seleciona
  `xmin` explicitamente no `FromSqlRaw` — o `SELECT *` do Postgres
  não inclui system columns, e o wrapper EF projeta `z.xmin` no outer
  query.
- `Program.cs` deixou de re-adicionar `appsettings.json` após
  `WebApplication.CreateBuilder` (CreateBuilder já faz isso) e agora
  pula `app.InitializeDatabaseAsync()` em ambiente `Testing` para
  permitir que o `CustomWebApplicationFactory` controle a migração
  contra o Testcontainers Postgres.
- `app.UseRateLimiter()` é pulado em ambiente `Testing` para que
  classes de teste paralelas não saturem o orçamento 5/5min por IP.
- Endpoint `GET /api/v1/auctions?status=...` agora aceita o status
  case-insensitive (lowercase é convenção REST).
- `CustomWebApplicationFactory` substitui o registro do `DbContext`
  diretamente no DI com a connection string do Testcontainers
  Postgres (em vez de depender da prioridade de fontes de
  configuração), tornando os testes paralelos resistentes a
  poluição de env vars.
- Brighter rebaixado de v10.0.2 para **v9.9.13** em todos os
  projetos (Application, Api, Infrastructure, Shared) para liberar
  a feature `Paramore.Brighter.Outbox.PostgreSql` — os helpers
  `UseOutbox` / `UseOutboxSweeper` foram removidos do
  `ServiceCollectionExtensions` no v10.0–v10.3 e só voltariam no
  v10.4+, que arrasta `Microsoft.Extensions.*` 10 e `Npgsql` 10
  (incompatíveis com nosso pin .NET 9 / EF Core 9.0.10 / Npgsql 9.0.4).
- `DomainEvent` e `ResultCommand` agora chamam `base(Guid.NewGuid())`
  em vez de `base(Id.Random())` — o helper `Id.Random()` da v10 não
  existe na v9 do Brighter.

### Removido
- Storage e dispatcher custom do outbox: `OutboxMessage` (entity),
  `IOutboxRepository`, `OutboxRepository`, `OutboxMessageMapping`,
  `OutboxDispatcherWorker` e a tabela `OutboxMessages` (migration
  `AddOutboxMessages` substituída por `AddBrighterOutbox`).
- Arquivos de payload ad-hoc (`auction.json`, `bid.json`,
  `login.json`, `test.json`).
- Scripts shell ad-hoc (`test-flow.sh`, `test-flow2.sh`,
  `test-redis.sh`, `test-all-endpoints.sh`).
- Artefato Linux package vendor (`packages-microsoft-prod.deb`).
- Guia de estudo Brighter/Darker commitado por engano.
- Cópia em português duplicada da especificação do assessment.
- Valor legado `AuctionStatus.Closed` (o único status terminal agora
  é `Finalized`).
- Propriedades aliases de domínio em `Auction` (`Name`,
  `StartingBid`, `EndDateTimeUtc`) que duplicavam os nomes
  canônicos. Os aliases sobraram de um refactor e não são mais
  necessários porque o contrato da API mora nos DTOs de
  command/view-model, não no aggregate.
- Overload `Auction.MarkWinner(winnerId, winningAmount)` duplicado;
  só o `MarkWinner(winningBidId, winnerId, winningAmount)` completo
  permanece.
- ADRs 0007 (RS256 JWT + JWKS) e 0008 (Argon2id) — substituídos
  pela escolha de auth simples documentada como trade-off no README.
- Endpoint `/.well-known/jwks.json` e `JwksEndpoints` (sem ADR
  associado depois da remoção 0007/0008).

[Unreleased]: https://github.com/mzet97/ZetAuction/compare/HEAD...HEAD
