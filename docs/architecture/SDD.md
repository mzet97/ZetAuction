# Software Design Document (SDD) - ZetAuction

**Versao:** 2.0
**Data:** 2026-05-07
**Projeto:** ZetAuction - Servico de Leiloes em Tempo Real
**Stack:** .NET 9, C#, EF Core 9.0.10, Postgres 16, Redis 7

> **Nota.** A v1 deste documento descrevia uma stack SQLite com tabela
> de locks distribuidos. A v2 reflete a implementacao atual: Postgres
> com optimistic concurrency via `xmin`, `FOR UPDATE SKIP LOCKED` para
> coordenacao multi-instancia, transactional outbox para eventos de
> dominio, JWT HS256 + BCrypt para auth, e pipeline OpenTelemetry
> completo (traces + metricas + logs). As decisoes que motivaram cada
> mudanca estao registradas nos [ADRs](../adr/README.md).

---

## 1. Introducao

### 1.1 Proposito

Este documento especifica o design de software para o ZetAuction, um servico de leiloes em tempo real desenvolvido como parte de uma avaliacao tecnica de backend senior. O SDD detalha a arquitetura, componentes, interfaces, dados e decisoes de design necessarias para implementar o sistema conforme os requisitos funcionais e nao-funcionais.

### 1.2 Escopo

O ZetAuction e uma Web API REST que permite:

- Cadastro e autenticacao de usuarios via JWT
- Criacao e gerenciamento de leiloes
- Registro de lances em tempo real com validacao de concorrencia
- Rate limiting distribuido por usuario
- Consulta do maior lance com otimizacao de cache
- Finalizacao automatica de leiloes vencidos
- Historico de lances com paginacao
- Observabilidade via health checks, metricas e logging estruturado
- Execucao em ambiente dockerizado com multiplas instancias

### 1.3 Definicoes e Abreviacoes

| Termo | Definicao |
|---|---|
| JWT | JSON Web Token |
| EF Core | Entity Framework Core |
| CQRS | Command Query Responsibility Segregation |
| DDD | Domain-Driven Design |
| TTL | Time To Live |
| WAL | Write-Ahead Logging |
| ZSET | Sorted Set (Redis) |
| UoW | Unit of Work |

### 1.4 Referencias

- [senior-backend-auction-assessment.pt-BR.md](D:/TI/git/ZetAuction/senior-backend-auction-assessment.pt-BR.md)
- Projeto de referencia: EChamado (padroes de codigo e arquitetura)

---

## 2. Visao Geral do Sistema

### 2.1 Contexto

O sistema opera em ambiente de multiplas instancias atras de um load balancer. Todas as instancias compartilham o mesmo Postgres 16 e o mesmo Redis 7. Essa arquitetura exige que todas as operacoes criticas (lances, rate limiting, finalizacao de leilao) sejam atomicas e consistentes entre instancias. A coordenacao multi-instancia se apoia em quatro mecanismos: optimistic concurrency via `xmin` para placement de lance, `FOR UPDATE SKIP LOCKED` para o worker de finalizacao, idempotencia de handlers para o dispatcher de outbox (Brighter `PostgreSqlOutbox` v9.9.13 nao serializa o claim), e Lua compare-and-set no Redis para o cache de maior lance.

### 2.2 Arquitetura de Alto Nivel

```
+------------------+     +------------------+     +------------------+
|     Cliente      |     |     Cliente      |     |     Cliente      |
|   (Web/Mobile)   |     |   (Web/Mobile)   |     |   (Web/Mobile)   |
+--------+---------+     +--------+---------+     +--------+---------+
         |                        |                        |
         +------------------------+------------------------+
                                  |
                    +-------------v--------------+
                    |    Load Balancer           |
                    |    (Round Robin)           |
                    +-------------+--------------+
                                  |
         +------------------------+------------------------+
         |                        |                        |
+--------v---------+     +--------v---------+     +--------v---------+
|  ZetAuction.Api  |     |  ZetAuction.Api  |     |  ZetAuction.Api  |
|   Instancia A    |     |   Instancia B    |     |   Instancia N    |
|                  |     |                  |     |                  |
|  +------------+  |     |  +------------+  |     |  +------------+  |
|  |   JWT      |  |     |  |   JWT      |  |     |  |   JWT      |  |
|  | Middleware |  |     |  | Middleware |  |     |  | Middleware |  |
|  +------------+  |     |  +------------+  |     |  +------------+  |
|  +------------+  |     |  +------------+  |     |  +------------+  |
|  |  Endpoints |  |     |  |  Endpoints |  |     |  |  Endpoints |  |
|  |  (Minimal) |  |     |  |  (Minimal) |  |     |  |  (Minimal) |  |
|  +------------+  |     |  +------------+  |     |  +------------+  |
|  +------------+  |     |  +------------+  |     |  +------------+  |
|  |  Handlers  |  |     |  |  Handlers  |  |     |  |  Handlers  |  |
|  |  (Brighter)|  |     |  |  (Brighter)|  |     |  |  (Brighter)|  |
|  +------------+  |     |  +------------+  |     |  +------------+  |
+--------+---------+     +--------+---------+     +--------+---------+
         |                        |                        |
         |    +-------------------+-------------------+    |
         |    |                                       |    |
         |    |     Postgres 16 (Fonte de Verdade)    |    |
         |    |     - Users                           |    |
         |    |     - Auctions (xmin concurrency)     |    |
         |    |     - Bids                            |    |
         |    |     - outbox_messages (Brighter)      |    |
         |    +---------------------------------------+    |
         |                        |                        |
         |    +-------------------+-------------------+    |
         |    |                                       |    |
         |    |     Redis (Coordenacao)               |    |
         |    |     - Rate Limiting (ZSET)            |    |
         |    |     - Cache Maior Lance               |    |
         |    +---------------------------------------+    |
         |                                                 |
         +-------------------------------------------------+
```

### 2.3 Componentes Principais

| Componente | Tecnologia | Responsabilidade |
|---|---|---|
| API | ASP.NET Core 9 | HTTP, routing, autenticacao, middlewares |
| Application | .NET 9 ClassLib | Casos de uso, CQRS, validacao |
| Domain | .NET 9 ClassLib | Entidades, regras de negocio, eventos |
| Infrastructure | .NET 9 ClassLib | Persistencia, cache, mensageria |
| Shared | .NET 9 ClassLib | Abstracoes, contratos, utilidades |
| Banco | Postgres 16 | Dados transacionais, outbox de eventos, optimistic concurrency via `xmin` |
| Cache/Coordenacao | Redis 7 | Rate limiting (sliding-window via Lua + ZSET), cache de maior lance (Lua CAS) |
| Observabilidade | OpenTelemetry + Grafana stack | Traces (Jaeger), metricas (Prometheus), logs (Loki) via OTLP |

---

## 3. Requisitos

### 3.1 Requisitos Funcionais

| ID | Descricao | Prioridade |
|---|---|---|
| RF-01 | Usuarios podem se registrar com login unico e senha | Alta |
| RF-02 | Usuarios autenticados recebem JWT para acesso | Alta |
| RF-03 | Usuarios autenticados podem criar leiloes | Alta |
| RF-04 | Leiloes possuem nome, lance inicial, incremento minimo e data de encerramento | Alta |
| RF-05 | Criador de leilao nao pode dar lances no proprio leilao | Alta |
| RF-06 | Usuarios autenticados podem listar leiloes com filtro por status e paginacao | Alta |
| RF-07 | Usuarios autenticados podem consultar detalhes de um leilao | Alta |
| RF-08 | Usuarios autenticados podem dar lances em leiloes ativos | Alta |
| RF-09 | Lance deve ser >= startingBid (primeiro) ou >= maiorLance + minBidIncrement | Alta |
| RF-10 | Lance em leilao expirado deve ser rejeitado | Alta |
| RF-11 | Rate limiting: maximo 5 lances por usuario em janela de 5 minutos | Alta |
| RF-12 | Rate limiting deve funcionar em multiplas instancias | Alta |
| RF-13 | Endpoint para consultar maior lance atual (frequente/polling) | Alta |
| RF-14 | Leiloes devem ser finalizados automaticamente ao atingir endDateTime | Alta |
| RF-15 | Finalizacao deve registrar vencedor (maior lance) ou sem lances | Alta |
| RF-16 | Leiloes finalizados nao aceitam novos lances | Alta |
| RF-17 | Historico de lances com paginacao | Media |
| RF-18 | Health checks e metricas | Media |
| RF-19 | Logging estruturado | Media |
| RF-20 | Ambiente dockerizado (Dockerfile + docker-compose) | Media |

### 3.2 Requisitos Nao-Funcionais

| ID | Descricao | Criterio |
|---|---|---|
| RNF-01 | Corretude em concorrencia | Apenas um lance valido por vez por leilao |
| RNF-02 | Consistencia multi-instancia | Todas as instancias veem o mesmo estado |
| RNF-03 | Disponibilidade | Degradacao elegante se Redis falhar |
| RNF-04 | Performance | Cache para maior lance reduz carga no banco |
| RNF-05 | Rastreabilidade | CorrelationId em todos os logs |
| RNF-06 | Testabilidade | Arquitetura em camadas permite testes unitarios e integracao |

---

## 4. Arquitetura de Software

### 4.1 Padrao Arquitetural

Clean Architecture com DDD e CQRS:

- **Domain**: Regras puras, sem dependencias externas
- **Application**: Orquestracao de casos de uso, CQRS
- **Infrastructure**: Implementacao de persistencia, cache, servicos externos
- **Api**: Interface HTTP, middlewares, configuracao
- **Shared**: Contratos compartilhados entre camadas

### 4.2 Diagrama de Camadas

```
+------------------+
|   ZetAuction.Api |  <-- HTTP, Endpoints, Middlewares
|   (Presentation) |
+--------+---------+
         |
+--------v---------+
| ZetAuction.App   |  <-- Commands, Queries, Handlers, ViewModels
|   (Application)  |
+--------+---------+
         |
+--------v---------+
| ZetAuction.Domain|  <-- Entities, Value Objects, Events, Validations
|     (Domain)     |
+--------+---------+
         |
+--------v---------+
| ZetAuction.Infra |  <-- DbContext, Repositories, Redis, Email
| (Infrastructure) |
+--------+---------+
         |
+--------v---------+
| ZetAuction.Shared|  <-- BaseResult, IEntity, IDomainEvent
|     (Shared)     |
+------------------+
```

### 4.3 Fluxo de Dependencia

- Api depende de Application, Domain, Infrastructure, Shared
- Application depende de Domain, Shared
- Infrastructure depende de Domain, Application, Shared
- Domain depende apenas de Shared
- Shared nao depende de nenhum outro projeto

---

## 5. Design de Componentes

### 5.1 ZetAuction.Shared

**Responsabilidade:** Contratos e abstracoes compartilhadas.

**Componentes:**

| Componente | Tipo | Descricao |
|---|---|---|
| Entity<T> | Classe Abstrata | Base para todas as entidades com Id, eventos, igualdade |
| AggregateRoot<T> | Classe Abstrata | Raiz de agregacao |
| AuditableEntity<T> | Classe Abstrata | Entidade com CreatedAtUtc e UpdatedAtUtc |
| SoftDeletableEntity<T> | Classe Abstrata | Entidade com suporte a soft delete |
| SoftDeletableAggregateRoot<T> | Classe Abstrata | Raiz de agregacao com soft delete |
| DomainEvent | Record | Evento de dominio base |
| IEntity | Interface | Contrato minimo de entidade |
| IDomainEvent | Interface | Contrato de evento de dominio |
| IAuditable | Interface | Contrato de auditoria |
| ISoftDeletable | Interface | Contrato de soft delete |
| BaseResult | Classe | Resultado padrao (success, message) |
| BaseResult<T> | Classe | Resultado com dados |
| BaseResultList<T> | Classe | Resultado paginado |
| PagedResult | Classe | Metadados de paginacao |
| IDateTimeProvider | Interface | Abstracao de data/hora para testes |
| SystemDateTimeProvider | Classe | Implementacao padrao |

### 5.2 ZetAuction.Domain

**Responsabilidade:** Regras de negocio, entidades, eventos, validacoes.

**Entidades:**

#### 5.2.1 User

| Atributo | Tipo | Regras |
|---|---|---|
| Id | Guid | PK, gerado no construtor |
| Login | string | Obrigatorio, unico, normalizado |
| PasswordHash | string | Obrigatorio, hash forte |
| CreatedAtUtc | DateTime | Preenchido automaticamente |

**Validacoes:**
- Login nao vazio, max 100 caracteres
- PasswordHash nao vazio

#### 5.2.2 Auction (Aggregate Root)

| Atributo | Tipo | Regras |
|---|---|---|
| Id | Guid | PK |
| CreatedByUserId | Guid | FK para User, obrigatorio |
| Title | string | Obrigatorio, max 300 caracteres (`name` no contrato da API) |
| Description | string | Obrigatorio, max 5000 caracteres |
| StartingPrice | decimal(18,2) | > 0 (`startingBid` no contrato) |
| MinBidIncrement | decimal(18,2) | > 0 |
| CurrentPrice | decimal(18,2) | Atualizado em `PlaceBid` |
| EndDate | timestamptz | Futuro em relacao a criacao (`endDateTime` no contrato) |
| Status | AuctionStatus | `Draft`, `Active` ou `Finalized` |
| WinnerId | Guid? | Preenchido na finalizacao |
| WinningBidId | Guid? | Preenchido na finalizacao |
| WinningAmount | decimal(18,2)? | Preenchido na finalizacao |
| FinalizedAtUtc | timestamptz? | Preenchido na finalizacao |
| RowVersion | uint | Mapeado para a system column `xmin` do Postgres (concurrency token) |
| CreatedAtUtc / UpdatedAtUtc | timestamptz | Auditoria automatica via interceptor |
| DeletedAtUtc / IsDeleted | timestamptz? / bool | Soft delete |

**Comportamentos:**
- `new Auction(...)`: Construtor cria em `Draft`
- `Activate()`: Transiciona `Draft` -> `Active`, emite `AuctionActivatedEvent`
- `PlaceBid(bid, dateTimeProvider)`: Valida regras de lance, atualiza `CurrentPrice`, emite `BidPlacedEvent`
- `Finalize(winningBid, dateTimeProvider)`: Status -> `Finalized`, popula `Winner*`/`FinalizedAtUtc`, emite `AuctionFinalizedEvent`

**Invariantes:**
- Criador nao pode dar lance no proprio leilao
- Leilao nao-`Active` nao aceita lances
- `EndDate` deve ser no futuro na criacao
- Lance deve ser `>= CurrentPrice + MinBidIncrement` (caso contrario `InsufficientBidAmountException`)
- Conflitos de `xmin` em `SaveChangesAsync` viram `ConcurrencyConflictException` (Polly retenta com backoff)

#### 5.2.3 Bid

| Atributo | Tipo | Regras |
|---|---|---|
| Id | Guid | PK |
| AuctionId | Guid | FK, obrigatorio |
| UserId | Guid | FK, obrigatorio |
| Amount | decimal | > 0 |
| CreatedAtUtc | DateTime | Preenchido automaticamente |

**Eventos de Dominio:**

| Evento | Disparado Quando | Dados |
|---|---|---|
| AuctionActivatedEvent | `Activate()` | AuctionId |
| BidPlacedEvent | Lance aceito | AuctionId, BidId, UserId, Amount |
| AuctionFinalizedEvent | `Finalize()` | AuctionId, WinningBidId, WinnerId, WinningAmount |

Todos os eventos sao mapeados para um `Paramore.Brighter.Message`
(Topic = FQN, Body = JSON, `Header.Bag["clr_type"]` =
AssemblyQualifiedName) e gravados em `outbox_messages` via
`PostgreSqlOutbox.AddAsync` na MESMA transacao que persiste o
aggregate, atraves do `DomainEventsSaveChangesInterceptor` e do
`EntityFrameworkPostgreSqlConnectionProvider`. O
`BrighterOutboxDispatcherWorker` consome via
`OutstandingMessagesAsync` e dispara via
`IAmACommandProcessor.PublishAsync<T>`.

### 5.3 ZetAuction.Application

**Responsabilidade:** Casos de uso, orquestracao, CQRS.

**Estrutura CQRS:**

```
UseCases/
  Auctions/
    Commands/
      CreateAuctionCommand.cs
      PlaceBidCommand.cs
      Handlers/
        CreateAuctionCommandHandler.cs
        PlaceBidCommandHandler.cs
    Queries/
      GetAuctionByIdQuery.cs
      GetHighestBidQuery.cs
      SearchAuctionsQuery.cs
      Handlers/
        GetAuctionByIdQueryHandler.cs
        GetHighestBidQueryHandler.cs
    ViewModels/
      AuctionViewModel.cs
      AuctionListViewModel.cs
```

**Commands (Brighter):**

| Command | Handler | Resultado |
|---|---|---|
| CreateAuctionCommand | CreateAuctionCommandHandler | BaseResult<Guid> |
| PlaceBidCommand | PlaceBidCommandHandler | BaseResult<Guid> |

**Queries (Darker):**

| Query | Handler | Resultado |
|---|---|---|
| GetAuctionByIdQuery | GetAuctionByIdQueryHandler | AuctionViewModel? |
| GetHighestBidQuery | GetHighestBidQueryHandler | BidViewModel? |
| SearchAuctionsQuery | SearchAuctionsQueryHandler | BaseResultList<AuctionListViewModel> |

**Pipeline Behaviors:**

| Behavior | Ordem | Funcao |
|---|---|---|
| RequestLogging | 0 | Log de entrada/saida |
| RequestValidation | 1 | Validacao FluentValidation |

### 5.4 ZetAuction.Infrastructure

**Responsabilidade:** Persistencia, cache, servicos externos.

**Componentes:**

| Componente | Tecnologia | Descricao |
|---|---|---|
| ZetAuctionDbContext | EF Core 9.0.10 + Npgsql | DbContext principal; xmin como concurrency token |
| Repository<T> | EF Core | Repositorio generico base |
| UnitOfWork | EF Core | Coordenacao de repositorios e transacoes |
| RedisService | StackExchange.Redis | Wrapper para operacoes Redis |
| DomainEventsSaveChangesInterceptor | EF Core | Dispatch de eventos de dominio |
| BrighterEventMapper | Paramore.Brighter | Mapeamento eventos de dominio -> Brighter |
| BrighterDomainEventDispatcher | Custom | Dispatcher de eventos via Brighter |

**Configuracao de DI:**

```csharp
services.AddScoped<DbContext, ApplicationDbContext>();
services.AddScoped<IUnitOfWork, UnitOfWork>();
services.AddScoped<IAuctionRepository, AuctionRepository>();
services.AddScoped<IBidRepository, BidRepository>();
services.AddScoped<IUserRepository, UserRepository>();
services.AddScoped<IRedisService, RedisService>();
services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
```

### 5.5 ZetAuction.Api

**Responsabilidade:** Interface HTTP, middlewares, configuracao.

**Endpoints:**

| Metodo | Rota | Auth | Descricao |
|---|---|---|---|
| POST | /api/v1/auth/register | Publico | Registro de usuario |
| POST | /api/v1/auth/login | Publico | Autenticacao (retorna JWT) |
| GET | /api/v1/auctions | JWT | Listagem com paginacao/filtro |
| GET | /api/v1/auctions/{id} | JWT | Detalhes do leilao |
| POST | /api/v1/auctions | JWT | Criar leilao |
| POST | /api/v1/auctions/{id}/bids | JWT | Dar lance |
| GET | /api/v1/auctions/{id}/bids/highest | JWT | Maior lance atual |
| GET | /api/v1/auctions/{id}/bids | JWT | Historico de lances |
| GET | /health | Publico | Health check |
| GET | /health/ready | Publico | Readiness probe |
| GET | /health/live | Publico | Liveness probe |
| GET | /health-ui | Publico | Dashboard de health |

**Middlewares:**

| Middleware | Ordem | Funcao |
|---|---|---|
| UseCors | 1 | CORS para cliente Blazor |
| UseRequestLogging | 2 | Log de requisicoes HTTP |
| UsePerformanceLogging | 3 | Alerta de requests lentas (>3s) |
| UseRouting | 4 | Routing |
| UseAuthentication | 5 | Validacao JWT |
| UseAuthorization | 6 | Autorizacao |

---

## 6. Design de Dados

### 6.1 Modelo Entidade-Relacionamento

```
[User] 1---* [Auction] (CreatedByUserId)
[Auction] 1---* [Bid]
[User] 1---* [Bid] (UserId)
```

### 6.2 Esquema do Banco (Postgres 16)

As migrations EF Core ficam em
`src/ZetAuction.Infrastructure/Persistence/Migrations/`. O snapshot do
modelo e a fonte de verdade para o schema; os DDLs abaixo sao a forma
em que o Postgres materializa esse modelo (com nomes de coluna em
PascalCase, mantidos pelo `ApplyConfigurationsFromAssembly`).

#### Tabela Users

```sql
CREATE TABLE "Users" (
    "Id"           UUID PRIMARY KEY,
    "Name"         VARCHAR(200) NOT NULL,
    "Email"        VARCHAR(320) NOT NULL,
    "PasswordHash" VARCHAR(200) NOT NULL,  -- BCrypt hash
    "Role"         VARCHAR(20)  NOT NULL,
    "CreatedAtUtc" TIMESTAMPTZ  NOT NULL,
    "UpdatedAtUtc" TIMESTAMPTZ  NOT NULL,
    "IsDeleted"    BOOLEAN      NOT NULL DEFAULT FALSE,
    "DeletedAtUtc" TIMESTAMPTZ  NULL
);
CREATE UNIQUE INDEX "IX_Users_Email" ON "Users"("Email");
```

#### Tabela Auctions

A coluna de sistema `xmin` (transaction id que atualizou a linha por
ultimo) e mapeada como concurrency token via
`builder.Property(a => a.RowVersion).HasColumnName("xmin").HasColumnType("xid")`
em `AuctionMapping`. Updates concorrentes que viram com `xmin` antigo
sao rejeitados pelo Postgres com `DbUpdateConcurrencyException`, que
o `PlaceBidCommandHandler` traduz em `ConcurrencyConflictException` e
o pipeline Polly retenta.

```sql
CREATE TABLE "Auctions" (
    "Id"              UUID PRIMARY KEY,
    "Title"           VARCHAR(300)  NOT NULL,
    "Description"     VARCHAR(5000) NOT NULL,
    "StartingPrice"   NUMERIC(18,2) NOT NULL,
    "MinBidIncrement" NUMERIC(18,2) NOT NULL,
    "CurrentPrice"    NUMERIC(18,2) NOT NULL,
    "EndDate"         TIMESTAMPTZ   NOT NULL,
    "Status"          VARCHAR(20)   NOT NULL,      -- "Draft" | "Active" | "Finalized"
    "CreatedByUserId" UUID          NOT NULL,
    "WinnerId"        UUID          NULL,
    "WinningBidId"    UUID          NULL,
    "WinningAmount"   NUMERIC(18,2) NULL,
    "FinalizedAtUtc"  TIMESTAMPTZ   NULL,
    "CreatedAtUtc"    TIMESTAMPTZ   NOT NULL,
    "UpdatedAtUtc"    TIMESTAMPTZ   NOT NULL,
    "DeletedAtUtc"    TIMESTAMPTZ   NULL,
    "IsDeleted"       BOOLEAN       NOT NULL DEFAULT FALSE
    -- xmin e a system column do Postgres, nao precisa ser declarada
);
CREATE INDEX "IX_Auctions_Status"          ON "Auctions"("Status");
CREATE INDEX "IX_Auctions_CreatedByUserId" ON "Auctions"("CreatedByUserId");
CREATE INDEX "IX_Auctions_Status_EndDate"  ON "Auctions"("Status", "EndDate");
```

#### Tabela Bids

```sql
CREATE TABLE "Bids" (
    "Id"           UUID PRIMARY KEY,
    "AuctionId"    UUID          NOT NULL REFERENCES "Auctions"("Id") ON DELETE CASCADE,
    "UserId"       UUID          NOT NULL,
    "Amount"       NUMERIC(18,2) NOT NULL,
    "CreatedAtUtc" TIMESTAMPTZ   NOT NULL,
    "UpdatedAtUtc" TIMESTAMPTZ   NOT NULL,
    "IsDeleted"    BOOLEAN       NOT NULL DEFAULT FALSE,
    "DeletedAtUtc" TIMESTAMPTZ   NULL
);
CREATE INDEX "IX_Bids_AuctionId" ON "Bids"("AuctionId");
CREATE INDEX "IX_Bids_UserId"    ON "Bids"("UserId");
```

#### Tabela outbox_messages (Brighter PostgreSqlOutbox)

A storage do outbox e a feature nativa
`Paramore.Brighter.Outbox.PostgreSql` v9.9.13 — schema fixado pelo
`PostgreSqlOutboxBulder.GetDDL`. Eventos de dominio sao mapeados para
o envelope `Message` do Brighter pelo
`DomainEventsSaveChangesInterceptor` e gravados via
`PostgreSqlOutbox.AddAsync` dentro da mesma transacao EF, atraves do
`EntityFrameworkPostgreSqlConnectionProvider` (bridge entre o
`DbContext` e o contrato `IPostgreSqlConnectionProvider` /
`IAmABoxTransactionConnectionProvider`). O
`BrighterOutboxDispatcherWorker` (`BackgroundService` proprio) faz
polling via `PostgreSqlOutbox.OutstandingMessagesAsync`, deserializa
`Body` resolvendo o `clr_type` do `Header.Bag` e chama
`IAmACommandProcessor.PublishAsync<T>` para dispatch in-process.
Brighter v9.9.13 **nao usa `FOR UPDATE SKIP LOCKED`** —
`OutstandingMessagesAsync` e um SELECT simples; multiplas replicas
podem ler a mesma linha antes do `MarkDispatchedAsync` commit.
Handlers sao idempotentes por contrato. Detalhes e racional em
[ADR-0003](../adr/0003-transactional-outbox.md).

```sql
-- Schema gerenciado por Paramore.Brighter.Outbox.PostgreSql v9.9.13
-- (PostgreSqlOutboxBulder.GetDDL). Migration: AddBrighterOutbox.
CREATE TABLE outbox_messages (
    Id            BIGSERIAL PRIMARY KEY,
    MessageId     UUID UNIQUE NOT NULL,
    Topic         VARCHAR(255) NULL,
    MessageType   VARCHAR(32)  NULL,   -- enum Brighter (MT_EVENT, ...)
    Timestamp     timestamptz  NULL,
    CorrelationId uuid         NULL,
    ReplyTo       VARCHAR(255) NULL,
    ContentType   VARCHAR(128) NULL,
    Dispatched    timestamptz  NULL,   -- NULL = outstanding
    HeaderBag     TEXT         NULL,   -- JSON; carrega clr_type, aggregate_id
    Body          TEXT         NULL    -- JSON do evento .NET
);
CREATE INDEX IX_outbox_messages_Outstanding_Timestamp
    ON outbox_messages (Timestamp)
    WHERE Dispatched IS NULL;
```

#### Locks distribuidos

A v1 deste documento previa uma tabela `DistributedLocks`. Foi
removida na Phase 3: a coordenacao de finalizacao usa
`SELECT ... FOR UPDATE SKIP LOCKED` direto na tabela `Auctions`
(`AuctionRepository.LockDueForFinalizationAsync`). O outbox abandonou
o lock por linha quando migramos para o `PostgreSqlOutbox` do
Brighter — handlers idempotentes substituem o lock como mecanismo
de exclusao mutua. Ver
[ADR-0003](../adr/0003-transactional-outbox.md).

### 6.3 Esquema Redis

#### Rate Limiting

- **Chave:** `ratelimit:{userId}`
- **Tipo:** Sorted Set (ZSET)
- **Membros:** Timestamps Unix (ms) de lances aceitos
- **TTL:** 5 minutos (expira apos inatividade)

**Operacao Lua:**

```lua
local key = KEYS[1]
local window = tonumber(ARGV[1])  -- 300000 ms (5 min)
local now = tonumber(ARGV[2])     -- timestamp atual ms
local limit = tonumber(ARGV[3])   -- 5

-- Remove entradas antigas
redis.call('ZREMRANGEBYSCORE', key, 0, now - window)

-- Conta entradas no periodo
local count = redis.call('ZCARD', key)

if count >= limit then
    return 0  -- Rejeitado
end

-- Adiciona novo timestamp
redis.call('ZADD', key, now, now)
redis.call('EXPIRE', key, window / 1000)

return 1  -- Aceito
```

#### Cache do Maior Lance

- **Chave:** `auction:{auctionId}:highest-bid`
- **Tipo:** Hash (Redis) com campos `id`, `userId`, `auctionId`, `amount`, `createdAtUtc`
- **TTL:** curto, suficiente para suportar polling em rajada — cliente
  ainda recebe `ETag` + `Cache-Control: private, max-age=1`
- **Estrategia de escrita:** Lua compare-and-set (`HSET` so se o
  `amount` recebido for estritamente maior que o stored). Lance "menor"
  que chega tarde nao corrompe o cache

---

## 7. Design de Interfaces

### 7.1 API REST

#### Autenticacao

**POST /api/v1/auth/register**

Request:
```json
{
  "name": "Alice Smith",
  "email": "alice@example.com",
  "password": "SenhaForte123!"
}
```

Response 201:
```json
{
  "success": true,
  "data": "550e8400-e29b-41d4-a716-446655440000",
  "message": "User created successfully."
}
```

**POST /api/v1/auth/login**

Request:
```json
{
  "email": "alice@example.com",
  "password": "SenhaForte123!"
}
```

Response 200 (token JWT HS256, alg fixado):
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "expiresAt": "2026-05-06T10:30:00Z",
    "user": { "id": "...", "email": "alice@example.com", "name": "Alice Smith", "role": "User" }
  },
  "message": "Authenticated successfully"
}
```

#### Leiloes

**POST /api/v1/auctions**

Request:
```json
{
  "name": "Leilao de Teste",
  "startingBid": 100.00,
  "minBidIncrement": 10.00,
  "endDateTime": "2026-05-10T18:00:00Z"
}
```

Response 201:
```json
{
  "success": true,
  "data": "550e8400-e29b-41d4-a716-446655440001",
  "message": "Auction created successfully"
}
```

**GET /api/v1/auctions?status=active&page=1&pageSize=20**

O parametro `status` aceita os valores `Draft`, `Active`, `Finalized`
case-insensitive (lowercase tambem funciona, convencao REST).

Response 200:
```json
{
  "success": true,
  "data": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440001",
      "name": "Leilao de Teste",
      "startingBid": 100.00,
      "minBidIncrement": 10.00,
      "currentPrice": 150.00,
      "endDateTime": "2026-05-10T18:00:00Z",
      "status": "Active",
      "createdAtUtc": "2026-05-05T10:00:00Z"
    }
  ],
  "pagedResult": {
    "currentPage": 1,
    "pageCount": 1,
    "pageSize": 20,
    "rowCount": 1
  }
}
```

**POST /api/v1/auctions/{auctionId}/bids**

Request:
```json
{
  "amount": 160.00
}
```

Response 201:
```json
{
  "success": true,
  "data": "550e8400-e29b-41d4-a716-446655440002",
  "message": "Bid placed successfully"
}
```

Response 429 (Rate Limit):
```json
{
  "success": false,
  "message": "Rate limit exceeded. Maximum 5 bids per 5 minutes.",
  "statusCode": 429
}
```

Response 409 (Lance Invalido):
```json
{
  "success": false,
  "message": "Bid amount must be at least 160.00",
  "statusCode": 409
}
```

**GET /api/v1/auctions/{auctionId}/bids/highest**

Response 200:
```json
{
  "success": true,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440002",
    "amount": 160.00,
    "userId": "550e8400-e29b-41d4-a716-446655440000",
    "createdAtUtc": "2026-05-05T10:30:00Z"
  }
}
```

Response 304 (Not Modified):
```
HTTP/1.1 304 Not Modified
ETag: "v:3"
```

### 7.2 Interfaces Internas

#### IRepository<TEntity>

```csharp
public interface IRepository<TEntity> : IDisposable
    where TEntity : class, IEntity
{
    Task AddAsync(TEntity entity);
    Task UpdateAsync(TEntity entity);
    Task RemoveAsync(Guid id);
    Task DisableAsync(Guid id);
    Task ActiveAsync(Guid id);
    Task<TEntity?> GetByIdAsync(Guid id);
    Task<IEnumerable<TEntity>> GetAllAsync();
    Task<BaseResultList<TEntity>> SearchAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        int pageSize = 10, int page = 1);
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null);
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);
    IQueryable<TEntity> GetAllQueryable();
}
```

#### IUnitOfWork

```csharp
public interface IUnitOfWork : IDisposable
{
    IAuctionRepository Auctions { get; }
    IBidRepository Bids { get; }
    IUserRepository Users { get; }
    
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
    Task<int> SaveChangesAsync();
}
```

#### IBidCacheService

`RedisCacheService` implementa via Lua compare-and-set; falhas
abrem circuit-breaker e o caller cai para query direta no banco.

```csharp
public interface IBidCacheService
{
    Task<BidViewModel?> GetHighestBidAsync(Guid auctionId);

    // HSET com Lua CAS: so atualiza se amount > stored
    Task SetHighestBidAsync(Guid auctionId, BidViewModel bid);

    Task InvalidateHighestBidAsync(Guid auctionId);
}
```

#### IRateLimiterService

`RedisRateLimiterService` implementa sliding-window via Lua + ZSET
com circuit-breaker para token bucket in-memory quando Redis cai.

```csharp
public interface IRateLimiterService
{
    Task<bool> IsAllowedAsync(string key, int maxRequests, int windowSeconds);
}
```

---

## 8. Design de Algoritmos e Logica

### 8.1 Algoritmo de Validacao de Lance

`PlaceBidCommandHandler` envolve a transacao num pipeline Polly que
retenta `ConcurrencyConflictException` (xmin mismatch). Rejeicoes de
dominio (`InsufficientBidAmount`, criador-bidder, leilao expirado)
nao sao retentadas — elas viram RFC 7807 ProblemDetails.

```
Funcao PlaceBidAsync(auctionId, userId, amount):
    1. Se !RedisRateLimiter.IsAllowedAsync($"user:{userId}:bids", limit=5, window=300s):
        retornar TooManyRequests (RFC 7807 type=rate-limited)
    2.
    3. -- Pipeline Polly (max 3 retries em ConcurrencyConflictException)
    4. retorno = ResiliencePipeline.ExecuteAsync(async () =>
    5.     Iniciar transacao
    6.
    7.     auction = AuctionRepository.GetByIdAsync(auctionId)  -- carrega xmin atual
    8.     Se auction == null: rollback; retornar NotFound
    9.     Se auction.CreatedByUserId == userId: rollback; retornar Forbidden
   10.     Se auction.Status != Active: rollback; retornar Conflict
   11.     Se auction.EndDate <= DateTimeProvider.UtcNow: rollback; retornar Conflict
   12.
   13.     bid = new Bid(auctionId, userId, amount, DateTimeProvider.UtcNow)
   14.     auction.PlaceBid(bid, DateTimeProvider)  -- valida >= currentPrice + minBidIncrement
   15.                                              -- emite BidPlacedEvent (DomainEvent)
   16.
   17.     BidRepository.AddAsync(bid)
   18.     AuctionRepository.UpdateAsync(auction)
   19.
   20.     try:
   21.         UnitOfWork.SaveChangesAsync()
   22.         -- DomainEventsSaveChangesInterceptor:
   23.         --   - popula audit fields
   24.         --   - mapeia BidPlacedEvent para Brighter Message e
   24a.        --     grava em outbox_messages via PostgreSqlOutbox.AddAsync
   25.         -- Postgres valida xmin: se outro INSERT/UPDATE alterou a linha,
   26.         -- DbUpdateConcurrencyException -> ConcurrencyConflictException
   27.         Commit
   28.     catch ConcurrencyConflictException:
   29.         Rollback
   30.         throw  -- Polly captura e retenta com backoff exponencial + jitter
   31.     catch DomainException:
   32.         Rollback
   33.         throw  -- nao retenta; vira ProblemDetails
   34. )
   35.
   36. -- Atualiza cache do maior lance via Lua compare-and-set
   37. -- (HSET so quando amount > stored amount); falhas de cache nao
   38. -- bloqueiam o response.
   39. await IBidCacheService.SetHighestBidAsync(auctionId, bidViewModel)
   40.
   41. retornar Success(bid)
```

### 8.2 Algoritmo de Finalizacao de Leiloes

A coordenacao entre replicas nao usa lock externo. Cada worker abre
sua propria transacao e claim um batch de leiloes vencidos via
`FOR UPDATE SKIP LOCKED`. Replicas concorrentes pulam silenciosamente
linhas que ja estao lockadas em outra transacao, entao nao ha
necessidade de coordenacao em nivel de aplicacao. Detalhes em
`AuctionRepository.LockDueForFinalizationAsync` e
`AuctionFinalizationWorker`.

```
Funcao FinalizeBatchAsync(batchSize, utcNow):
    1. Iniciar transacao
    2.
    3. lockedAuctions = SELECT *, xmin
                        FROM "Auctions"
                        WHERE "Status" = 'Active'
                          AND "EndDate" <= utcNow
                          AND "IsDeleted" = FALSE
                        ORDER BY "EndDate"
                        LIMIT batchSize
                        FOR UPDATE SKIP LOCKED
    4.
    5. Se lockedAuctions vazio: rollback; retornar 0
    6.
    7. Carregar bids dos leiloes claimed
    8.
    9. Para cada auction em lockedAuctions:
        10. winningBid = bids.Where(AuctionId=auction.Id).OrderByDesc(Amount).First()
        11. auction.Finalize(winningBid, DateTimeProvider)
        12. -- DomainEventsSaveChangesInterceptor mapeia AuctionFinalizedEvent
        13. -- para um Brighter Message e grava em outbox_messages via
        13a.-- PostgreSqlOutbox.AddAsync, dentro da mesma transacao
    14.
    15. Commit transacao
    16. -- Linhas claimed liberam o lock automaticamente; outras replicas
    17. -- continuam servindo seus proprios batches
    18.
    19. retornar lockedAuctions.Count
```

O dispatcher do outbox (`BrighterOutboxDispatcherWorker`) **nao** usa
o mesmo padrao — Brighter v9.9.13 prove o `PostgreSqlOutbox` que
implementa o claim com SELECT simples (`OutstandingMessagesAsync`
filtra por `Dispatched IS NULL`). Multi-instancia at-least-once
delivery e suportado pela combinacao de durabilidade da linha +
idempotencia obrigatoria dos handlers.

### 8.3 Algoritmo de Rate Limiting (Redis Lua)

```
Funcao IsAllowed(userId):
    1. key = "ratelimit:" + userId
    2. window = 300000  -- 5 minutos em ms
    3. now = currentTimeMs()
    4. limit = 5
    5. 
    6. -- Executar script Lua atomico
    7. resultado = Redis.Eval(
           "ZREMRANGEBYSCORE", key, 0, now - window;
           "ZCARD", key;
           Se count >= limit: retornar 0;
           "ZADD", key, now, now;
           "EXPIRE", key, window/1000;
           retornar 1)
    8. 
    9. retornar resultado == 1
```

---

## 9. Design de Seguranca

### 9.1 Autenticacao

- JWT assinado com HMAC-SHA256 (HS256)
- Chave simetrica `Jwt:SecretKey` injetada via env var (validada >= 32
  caracteres no startup pelo `JwtService`)
- Expiracao: 1 hora (configuravel via `Jwt:ExpirationHours`)
- Payload: `sub` (userId), `email`, `role`, `iat`, `exp`
- `ValidAlgorithms` e fixado em `HmacSha256` no `AuthConfig` para
  fechar a porta a algorithm confusion (`alg=none`, troca de algoritmo)
- `ClockSkew = TimeSpan.Zero` para evitar tokens stale alem da janela
- Sem refresh token (escopo do assessment)
- Evolucao planejada (registrada como trade-off no README): RS256 +
  JWKS quando a validacao precisar ser delegada a outro resource server

### 9.2 Autorizacao

- Todos os endpoints de leilao exigem JWT valido
- Verificacao de ownership: criador nao pode dar lance
- Rate limiting por usuario (5 lances / 5 min)

### 9.3 Protecao de Dados

- Senhas hasheadas com BCrypt
- Nunca expor PasswordHash nas respostas
- Validacao de entrada em todos os endpoints

### 9.4 Headers de Seguranca

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Strict-Transport-Security` (em producao)

---

## 10. Design de Performance

### 10.1 Otimizacoes

| Otimizacao | Onde | Impacto |
|---|---|---|
| Cache Redis maior lance | GET /bids/highest | Reduz queries no banco em 90%+ |
| AsNoTracking em queries | Repository.SearchAsync | Reduz overhead do EF Core |
| Indices compostos | Postgres (`Status, EndDate`) | Acelera scan do worker de finalizacao |
| Partial index | `outbox_messages WHERE Dispatched IS NULL` | Dispatcher so varre mensagens outstanding |
| `xmin` rowversion | Optimistic concurrency | Conflitos rejeitados pelo banco; Polly retenta com jitter |
| `FOR UPDATE SKIP LOCKED` | Finalizer (Auctions) | Replicas claim batches sem coordenacao adicional. Outbox usa idempotencia de handler em vez disso (Brighter v9.9.13). |
| MVCC do Postgres | Reads sem bloquear writes | Polling agressivo de `/highest` nao trava `INSERT` em `Bids` |

### 10.2 Cache

| Cache | TTL | Invalidacao |
|---|---|---|
| Maior lance (Redis) | 1-3 segundos | Apos lance aceito |
| Rate limit (Redis) | 5 minutos | TTL automatico |

### 10.3 Degradacao Elegante

| Falha | Comportamento |
|---|---|
| Redis indisponivel | Cache de maior lance cai para query direta no banco; rate limiter abre circuit-breaker e cai para token bucket in-memory (degradacao **fechada**, nunca aberta — ver `RedisRateLimiterService` e [runbook](../runbooks/redis-outage.md)) |
| Postgres com pressao | Pipeline Polly retenta `ConcurrencyConflictException`; outbox/finalizer descansam o batch com backoff |
| JWT invalido / `alg` errado | 401 Unauthorized (RFC 7807 ProblemDetails) |
| Outbox stuck | Linha permanece com `Dispatched IS NULL`; logs do worker e o counter `zetauction_outbox_failed` sao a unica trilha de falha (Brighter v9.9.13 nao persiste contagem de falha). Inspecao via `outbox_messages` + replay manual em [runbook](../runbooks/outbox-stuck-messages.md) |

---

## 11. Design de Testes

### 11.1 Estrategia

| Tipo | Escopo | Ferramenta |
|---|---|---|
| Unitario | Entidades, validacoes, handlers isolados | xUnit + NSubstitute |
| Integracao | Fluxo completo API -> Banco | WebApplicationFactory + Testcontainers Postgres 16 + Respawn |
| Chaos / concorrencia | Lances concorrentes, cache regression, rate limiter sob burst | xUnit + `Task.WhenAll` contra Testcontainers |
| Concorrencia | Multiplas requisicoes paralelas | HttpClient paralelo |

### 11.2 Cenarios de Teste

**Unitarios:**
- Criador nao pode dar lance no proprio leilao
- Primeiro lance abaixo de startingBid e rejeitado
- Lance abaixo de maiorLance + minBidIncrement e rejeitado
- Leilao expirado rejeita lances
- Finalizacao com lances escolhe maior
- Finalizacao sem lances marca sem vencedor
- Rate limiter rejeita sexta tentativa

**Integracao:**
- Registro e login retornam token valido
- Criacao e consulta de leilao
- Listagem com paginacao e filtro
- Lance valido atualiza maior lance
- Requisicoes concorrentes: apenas um lance vence
- Rate limiting funciona com chamadas paralelas
- Leilao vencido rejeita lance antes do job
- Job de finalizacao e idempotente
- Cache invalidado apos lance
- Health check retorna status correto

---

## 12. Design de Implantacao

### 12.1 Dockerfile

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "ZetAuction.Api.dll"]
```

### 12.2 Docker Compose

A composicao real esta em `docker-compose.yml` na raiz do repo. Alem
da API ela sobe Postgres 16, Redis 7, OTel Collector, Jaeger,
Prometheus, Loki e Grafana, alem de um job `api-migrate` que aplica
migrations antes da API arrancar (substituindo o `EnsureCreatedAsync`
da v1). Esqueleto:

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: zetauction
      POSTGRES_PASSWORD: zetauction
      POSTGRES_DB: zetauction
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U zetauction -d zetauction"]

  redis:
    image: redis:7-alpine
    command: redis-server --appendonly yes --maxmemory 256mb --maxmemory-policy allkeys-lru

  api-migrate:
    build: { context: ., dockerfile: Dockerfile }
    command: ["dotnet", "ZetAuction.Api.dll", "--migrate-only"]
    environment:
      ConnectionStrings__DefaultConnection: Host=postgres;...;Username=zetauction;Password=zetauction
      Jwt__SecretKey: ${JWT_SECRET:-please-override-in-production-with-a-real-secret-of-at-least-32-chars}
    depends_on: { postgres: { condition: service_healthy } }
    restart: "no"

  api:
    build: { context: ., dockerfile: Dockerfile }
    ports: ["8080:8080"]
    environment:
      ConnectionStrings__DefaultConnection: Host=postgres;...
      ConnectionStrings__Redis: redis:6379
      Jwt__SecretKey: ${JWT_SECRET:-...}
      Otel__OtlpEndpoint: http://otel-collector:4317
    depends_on:
      postgres:    { condition: service_healthy }
      redis:       { condition: service_healthy }
      api-migrate: { condition: service_completed_successfully }
    security_opt: [ "no-new-privileges:true" ]
```

A stack completa de observabilidade (OTel Collector, Jaeger, Prometheus,
Loki, Grafana) esta no compose mas e omitida aqui por brevidade.

### 12.3 Variaveis de Ambiente

| Variavel | Descricao | Exemplo |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Connection string do Postgres | `Host=postgres;Port=5432;Database=zetauction;Username=zetauction;Password=zetauction;Include Error Detail=true` |
| `ConnectionStrings__Redis` | Connection string do Redis | `redis:6379` |
| `Jwt__Issuer` / `Jwt__Audience` | Validacao do JWT | `ZetAuction` / `ZetAuctionClient` |
| `Jwt__SecretKey` | Segredo HMAC do JWT (>= 32 chars) | injetado via Kubernetes Secret / Vault em prod |
| `Jwt__ExpirationHours` | Lifetime do token (default 1) | `1` |
| `Otel__ServiceName` | Resource name OpenTelemetry | `zetauction-api` |
| `Otel__OtlpEndpoint` | Endpoint gRPC do OTel Collector | `http://otel-collector:4317` |
| `Serilog__MinimumLevel__Default` | Nivel minimo de log | `Information` |

---

## 13. Design de Observabilidade

### 13.1 Logging

**Estrutura:**

```json
{
  "Timestamp": "2026-05-05T10:30:00.000Z",
  "Level": "Information",
  "MessageTemplate": "Bid placed successfully",
  "Properties": {
    "CorrelationId": "abc123",
    "UserId": "550e8400-e29b-41d4-a716-446655440000",
    "AuctionId": "550e8400-e29b-41d4-a716-446655440001",
    "BidId": "550e8400-e29b-41d4-a716-446655440002",
    "Amount": 160.00,
    "Endpoint": "POST /api/v1/auctions/550e8400-e29b-41d4-a716-446655440001/bids",
    "DurationMs": 45
  }
}
```

**Niveis:**

| Nivel | Evento |
|---|---|
| Information | Operacoes normais (lance aceito, leilao criado) |
| Warning | Tentativas rejeitadas (rate limit, lance invalido) |
| Error | Falhas inesperadas |

### 13.2 Metricas

| Metrica | Tipo | Endpoint |
|---|---|---|
| bids_accepted_total | Counter | /metrics |
| bids_rejected_total | Counter | /metrics |
| rate_limit_exceeded_total | Counter | /metrics |
| auctions_finalized_total | Counter | /metrics |
| request_duration_seconds | Histogram | /metrics |

### 13.3 Health Checks

| Endpoint | Proposito |
|---|---|
| `/health` | Status geral (Postgres + Redis) |
| `/health/ready` | Pronto para receber trafego |
| `/health/live` | Aplicacao respondendo |
| `/health-ui` | Dashboard visual |

---

## 14. Decisoes de Design e Trade-offs

### 14.1 Decisoes Tomadas

| Decisao | Justificativa |
|---|---|
| Postgres 16 + Redis 7 | xmin para optimistic concurrency, `FOR UPDATE SKIP LOCKED` para o finalizer, e `Paramore.Brighter.Outbox.PostgreSql` para o outbox transacional — features especificas de Postgres ([ADR-0001](../adr/0001-postgres-primary-store.md)) |
| JWT HS256 + BCrypt | Stack de auth simples e padrao para o escopo do assessment; RS256/JWKS + Argon2id ficou registrado como evolucao planejada |
| Rate limiting em Redis (sliding-window via Lua + ZSET) | Atomico entre instancias; sub-ms latencia; circuit breaker fecha (nao abre) para token bucket in-memory quando Redis cai |
| Cache Redis com Lua compare-and-set | HSET so quando o valor recebido e estritamente maior que o armazenado; lance fora de ordem nao corrompe o cache ([ADR-0004](../adr/0004-redis-highest-bid-cache.md)) |
| Brighter v9.9.13 (commands + outbox) + Darker (queries) | Brighter prove tanto o command bus in-process quanto o `PostgreSqlOutbox` (storage transacional + sweeper). Darker mantem queries desacopladas (sem domain events, sem transacao) ([ADR-0006](../adr/0006-brighter-darker-cqrs.md)) |
| Transactional outbox via `Paramore.Brighter.Outbox.PostgreSql` + finalizer com `FOR UPDATE SKIP LOCKED` | Multi-instancia at-least-once delivery sem coordenador externo. Brighter possui storage e dispatcher; nosso `EntityFrameworkPostgreSqlConnectionProvider` faz a bridge da transacao EF para o outbox no SaveChanges. Handlers obrigatoriamente idempotentes ([ADR-0003](../adr/0003-transactional-outbox.md)) |
| Pipeline Polly em ConcurrencyConflictException | Retry com backoff + jitter; rejeicoes de dominio (`InsufficientBid`) NAO sao retentadas ([ADR-0005](../adr/0005-polly-retry-on-conflict.md)) |
| RFC 7807 ProblemDetails em todo nao-2xx | URIs `type` estaveis no contrato da API permitem clients programarem ([ADR-0009](../adr/0009-rfc7807-problem-details.md)) |

### 14.2 Trade-offs

| Aspecto | Escolha | Alternativa | Impacto |
|---|---|---|---|
| Banco | Postgres 16 | SQLite (proposta original do assessment) | Mais infra para subir local mas viabiliza correcao multi-instancia |
| Rate limiting | Redis | Banco | Adiciona infraestrutura, mas melhora performance |
| Cache | Redis distribuido | Cache local por instancia | Consistencia entre instancias vs simplicidade |
| Auth | JWT simples | ASP.NET Core Identity completo | Menos features, mas mais simples |
| Finalizacao | Background job | Event-driven | Atraso de segundos aceitavel para simplicidade |

### 14.3 Premissas

- Todas as instancias apontam para o mesmo cluster Postgres 16
- Todas as instancias acessam o mesmo Redis
- Horarios sempre em UTC
- Valores monetarios em decimal
- JWT chave simetrica segura em producao

---

## 15. Glossario

| Termo | Definicao |
|---|---|
| Aggregate Root | Entidade raiz de um agregado DDD; unico ponto de acesso |
| CQRS | Separacao de operacoes de leitura e escrita |
| Domain Event | Evento que representa algo que aconteceu no dominio |
| Handler | Classe que processa um comando ou query |
| JWT | Token assinado para autenticacao stateless |
| Lua Script | Script executado atomicamente no Redis |
| Soft Delete | Marcacao logica de exclusao (nao remove do banco) |
| UoW | Unit of Work; padrao para coordenar transacoes |
| MVCC | Multi-Version Concurrency Control; mecanismo do Postgres que separa leituras de escritas |
| `xmin` | System column do Postgres com o transaction id que atualizou a linha por ultimo (concurrency token) |
| `FOR UPDATE SKIP LOCKED` | Clausula Postgres que pula linhas ja lockadas em outra transacao (sem esperar) |
| Outbox | Padrao transacional que escreve eventos em tabela na mesma transacao do aggregate, com dispatcher async |
| ZSET | Sorted Set; estrutura do Redis ordenada por score |

---

## 16. Historico de Revisoes

| Versao | Data | Autor | Descricao |
|---|---|---|---|
| 1.0 | 2026-05-05 | SWE | Versao inicial do SDD |

---

**Fim do Documento**
