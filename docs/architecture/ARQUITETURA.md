# Arquitetura da Solucao - ZetAuction

Este documento descreve a arquitetura do **ZetAuction - Servico de
Leiloes**.

> **Nota.** A v1 desta arquitetura previa Postgres + tabela de locks
> distribuidos. A versao atual usa **Postgres 16** como fonte de
> verdade (`xmin` para optimistic concurrency, `FOR UPDATE SKIP LOCKED`
> para coordenacao multi-instancia, transactional outbox), **Redis 7**
> para rate limit + cache, **JWT HS256 + BCrypt** para auth e pipeline
> **OpenTelemetry** completo. As decisoes de cada componente estao em
> [`docs/adr/`](../adr/README.md).
>
> **Sobre Brighter / Darker.** Brighter v9.9.13 e usado como bus de
> comandos in-process e Darker para queries. A **storage do outbox e
> a feature nativa `Paramore.Brighter.Outbox.PostgreSql`** (tabela
> `outbox_messages`); o `BrighterOutboxDispatcherWorker` consome via
> `PostgreSqlOutbox.OutstandingMessagesAsync` e republica via
> `IAmACommandProcessor.PublishAsync<T>`. A bridge para a transacao
> EF e o `EntityFrameworkPostgreSqlConnectionProvider`. Ver
> [ADR-0003](../adr/0003-transactional-outbox.md).

## Objetivos Arquiteturais

- Garantir corretude dos lances mesmo com requisicoes concorrentes.
- Suportar multiplas instancias da API atras de um balanceador de carga.
- Evitar estado critico em memoria local da instancia.
- Manter a solucao simples o suficiente para o escopo do desafio, mas com decisoes explicitas de evolucao.
- Documentar trade-offs de consistencia, cache, persistencia e finalizacao assincroma.

## Visao Geral

A solucao e uma Web API .NET 9 organizada em camadas, usando EF Core
9.0.10 com Postgres 16 como banco transacional e Redis 7 para rate
limiting distribuido e cache compartilhado entre instancias.

```text
Clientes
  |
  v
Load Balancer
  |
  +--> ZetAuction.Api instancia A
  +--> ZetAuction.Api instancia B
  +--> ZetAuction.Api instancia N
             |                |
             v                v
        Postgres 16          Redis 7
        (xmin / outbox /     (Lua sliding-window
        FOR UPDATE SKIP       rate limit + cache
        LOCKED)               de maior lance)
```

Postgres 16 e a fonte de verdade para dados transacionais (Users,
Auctions com `xmin`, Bids, e a tabela `outbox_messages` do
Brighter). Redis 7 cuida de
coordenacao multi-instancia: rate limiting (sliding-window via Lua +
ZSET) e cache do maior lance (Lua compare-and-set). Essa separacao
reduz carga no banco e melhora throughput de leitura em polling
frequente.

## Organizacao dos Projetos

```text
src/
  ZetAuction.Api/
  ZetAuction.Application/
  ZetAuction.Domain/
  ZetAuction.Infrastructure/
  ZetAuction.Shared/
tests/
  ZetAuction.UnitTests/
  ZetAuction.IntegrationTests/
```

### ZetAuction.Shared

Projeto compartilhado contendo abstracoes, contratos e utilidades usadas por todas as camadas.

**Responsabilidades:**

- Entidades base (`Entity`, `AggregateRoot`, `AuditableEntity`, `SoftDeletableEntity`)
- Interfaces de dominio (`IEntity`, `IDomainEvent`, `IAuditable`, `ISoftDeletable`)
- Respostas padrao (`BaseResult`, `BaseResultList`, `PagedResult`)
- Servicos compartilhados (`IDateTimeProvider`)
- Value objects base

**Padroes:**

- Entidades genericas com validacao via FluentValidation
- Eventos de dominio como `record` com `Guid EventId` e `DateTime OccurredOnUtc`
- `IDateTimeProvider` para testabilidade de timestamps

### ZetAuction.Domain

Camada de dominio puro, sem dependencias externas (exceto `ZetAuction.Shared` e FluentValidation).

**Estrutura:**

```text
Domains/
  Auctions/
    Auction.cs              # Aggregate Root
    Entities/
      Bid.cs
    Events/
      AuctionCreated.cs
      AuctionFinalized.cs
      BidPlaced.cs
    Validations/
      AuctionValidation.cs
  Users/
    User.cs
    Validations/
      UserValidation.cs
Repositories/
  IRepository.cs
  IUnitOfWork.cs
  Auctions/
    IAuctionRepository.cs
Services/
  Interface/
    IRedisService.cs
Validations/
  Validator.cs
Exceptions/
  NotFoundException.cs
  ValidationException.cs
```

**Padroes:**

- **Aggregate Roots**: Herdam de `AggregateRoot<T>` ou `SoftDeletableAggregateRoot<T>`
- **Factory Methods**: `Create()` e `CreateForTest()` para construcao controlada
- **Validacao**: FluentValidation no construtor via `Validate()`
- **Eventos de Dominio**: Disparados via `AddEvent()` em metodos de negocio
- **Propriedades**: `private set` para imutabilidade, modificacao apenas via metodos de dominio
- **Soft Delete**: `ISoftDeletable` com `DeletedAtUtc` e `IsDeleted`

**Exemplo de Entidade** (campos canonicos do dominio; o contrato da
API expoe `name`, `startingBid`, `endDateTime` via DTOs em
`Application` para casar com o spec do assessment):

```csharp
public class Auction : SoftDeletableAggregateRoot<Auction>
{
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public decimal StartingPrice { get; private set; }
    public decimal MinBidIncrement { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public DateTime EndDate { get; private set; }
    public AuctionStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? WinnerId { get; private set; }
    public Guid? WinningBidId { get; private set; }
    public decimal? WinningAmount { get; private set; }
    public DateTime? FinalizedAtUtc { get; private set; }

    // Mapeada para a system column xmin do Postgres em AuctionMapping;
    // EF Core usa como concurrency token, conflitos viram
    // DbUpdateConcurrencyException -> ConcurrencyConflictException.
    public uint RowVersion { get; private set; }

    public Auction(
        string title,
        string description,
        decimal startingPrice,
        decimal minBidIncrement,
        DateTime endDate,
        Guid createdByUserId)
    {
        Id = Guid.NewGuid();
        Title = title;
        Description = description;
        StartingPrice = startingPrice;
        MinBidIncrement = minBidIncrement;
        CurrentPrice = startingPrice;
        EndDate = endDate;
        Status = AuctionStatus.Draft;  // Activate() e chamada explicitamente
        CreatedByUserId = createdByUserId;
    }

    public void Activate()
    {
        if (Status != AuctionStatus.Draft) throw new DomainException("...");
        Status = AuctionStatus.Active;
        AddDomainEvent(new AuctionActivatedEvent(Id));
    }

    public void PlaceBid(Bid bid, IDateTimeProvider dateTimeProvider)
    {
        if (Status != AuctionStatus.Active)
            throw new DomainException("Auction is not active");
        if (EndDate <= dateTimeProvider.UtcNow)
            throw new DomainException("Auction has ended");
        if (bid.UserId == CreatedByUserId)
            throw new DomainException("Creator cannot bid on own auction");

        var minimumValid = CurrentPrice + MinBidIncrement;
        if (bid.Amount < minimumValid)
            throw new InsufficientBidAmountException(bid.Amount, minimumValid);

        CurrentPrice = bid.Amount;
        AddDomainEvent(new BidPlacedEvent(Id, bid.Id, bid.UserId, bid.Amount));
    }

    public void Finalize(Bid? winningBid, IDateTimeProvider dateTimeProvider)
    {
        if (Status != AuctionStatus.Active) throw new DomainException("...");
        Status = AuctionStatus.Finalized;
        FinalizedAtUtc = dateTimeProvider.UtcNow;
        if (winningBid != null)
        {
            WinnerId = winningBid.UserId;
            WinningBidId = winningBid.Id;
            WinningAmount = winningBid.Amount;
        }
        AddDomainEvent(new AuctionFinalizedEvent(Id, WinningBidId, WinnerId, WinningAmount));
    }
}
```

### ZetAuction.Application

Camada de aplicacao contendo casos de uso, handlers e orquestracao.

**Estrutura:**

```text
Configuration/
  DependencyInjection.cs
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
    Notifications/
      AuctionFinalizedNotification.cs
      Handlers/
        AuctionFinalizedEmailHandler.cs
  Users/
    Commands/
    Queries/
    ViewModels/
Common/
  Behaviours/
    ValidationBehavior.cs
    RequestLoggingBehaviour.cs
  Messaging/
    BrighterRequest.cs
Services/
  IJwtService.cs
```

**Padroes:**

- **Commands**: Herdam de `BrighterRequest<T>` (Paramore.Brighter) para CQRS
- **Queries**: Usam Paramore.Darker ou MediatR
- **Handlers**: Primary constructor com DI, `[RequestLogging]` e `[RequestValidation]` attributes
- **ViewModels**: `record` com propriedades imutaveis
- **Validacao**: FluentValidation + pipeline behavior

**Exemplo de Handler** (forma simplificada — o handler real envolve
todo o caminho de gravacao num pipeline Polly que retenta
`ConcurrencyConflictException` com backoff + jitter; rejeicoes de
dominio nao sao retentadas):

```csharp
public class PlaceBidCommandHandler : RequestHandlerAsync<PlaceBidCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IRateLimiterService _rateLimiterService;
    private readonly IBidCacheService _bidCacheService;
    private readonly ResiliencePipeline _concurrencyRetryPipeline;

    public override async Task<PlaceBidCommand> HandleAsync(
        PlaceBidCommand command, CancellationToken cancellationToken = default)
    {
        // Rate limit por usuario antes de qualquer custo de DB.
        if (!await _rateLimiterService.IsAllowedAsync(
                $"user:{command.UserId}:bids", maxRequests: 5, windowSeconds: 300))
        {
            command.Result = BaseResult.Fail("Rate limit exceeded.");
            return await base.HandleAsync(command, cancellationToken);
        }

        var outcome = await _concurrencyRetryPipeline.ExecuteAsync(async _ =>
            await PlaceBidWithTransactionAsync(command, cancellationToken),
            cancellationToken);

        // Cache do maior lance via Lua compare-and-set: HSET so se o
        // valor recebido > armazenado. Falha de cache nao bloqueia o
        // response.
        await _bidCacheService.SetHighestBidAsync(command.AuctionId, outcome.BidViewModel);

        command.Result = BaseResult.Ok(outcome.BidId);
        return await base.HandleAsync(command, cancellationToken);
    }

    private async Task<BidPlacementOutcome> PlaceBidWithTransactionAsync(
        PlaceBidCommand command, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var auction = await _unitOfWork.Auctions.GetByIdAsync(command.AuctionId, cancellationToken);
            if (auction == null) throw new AuctionNotFoundException(command.AuctionId);

            var bid = new Bid(command.AuctionId, command.UserId, command.Amount, _dateTimeProvider.UtcNow);
            auction.PlaceBid(bid, _dateTimeProvider);  // valida + emite BidPlacedEvent

            await _unitOfWork.Bids.AddAsync(bid);
            await _unitOfWork.Auctions.UpdateAsync(auction);

            await _unitOfWork.CommitAsync(cancellationToken);
            // DomainEventsSaveChangesInterceptor mapeia o BidPlacedEvent
            // para um Brighter Message e grava em outbox_messages via
            // PostgreSqlOutbox.AddAsync, dentro desta mesma transacao.
            return new BidPlacementOutcome(bid.Id, ToViewModel(bid));
        }
        catch (ConcurrencyConflictException)
        {
            // CommitAsync ja fez rollback; rethrow para Polly retentar.
            throw;
        }
        catch (DomainException)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
```

### ZetAuction.Infrastructure

Camada de infraestrutura implementando persistencia, cache, mensageria e servicos externos.

**Estrutura:**

```text
Configuration/
  DependencyInjectionConfig.cs
  DatabaseInitializer.cs
  IdentityConfig.cs
  RedisConfig.cs
  MessageBusConfig.cs
Persistence/
  ApplicationDbContext.cs
  ApplicationDbContextFactory.cs
  DomainEventsSaveChangesInterceptor.cs
  Mappings/
    AuctionMapping.cs
    BidMapping.cs
  Migrations/
  Repositories/
    Repository.cs
    UnitOfWork.cs
    Auctions/
      AuctionRepository.cs
Redis/
  RedisService.cs
  MemoryOutputCacheStore.cs
  RedisOutputCacheStore.cs
Events/
  BrighterEventMapper.cs
  BrighterDomainEventDispatcher.cs
Email/
Services/
```

**Padroes:**

- **DbContext**: Herda de `IdentityDbContext`, usa `ApplyConfigurationsFromAssembly`
- **Repository**: Classe abstrata generica `Repository<TEntity>` implementando `IRepository<TEntity>`
- **UnitOfWork**: Agrupa repositorios e gerencia transacoes
- **Interceptors**: `DomainEventsSaveChangesInterceptor` para dispatch de eventos
- **Redis**: `IRedisService` wrapper sobre `IDistributedCache`
- **DI**: Metodos de extensao `ResolveDependenciesInfrastructure()`

**Exemplo de Repository:**

```csharp
public abstract class Repository<TEntity> : IRepository<TEntity>
    where TEntity : class, IEntity
{
    protected readonly ApplicationDbContext Db;
    protected readonly DbSet<TEntity> DbSet;
    protected readonly IDateTimeProvider DateTimeProvider;

    protected Repository(ApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        Db = db ?? throw new ArgumentNullException(nameof(db));
        DateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        DbSet = db.Set<TEntity>();
    }

    public virtual async Task AddAsync(TEntity entity)
    {
        if (entity is IAuditable auditable)
            auditable.MarkCreated(DateTimeProvider.UtcNow);
        
        await DbSet.AddAsync(entity);
        await Db.SaveChangesAsync();
    }

    public virtual async Task<BaseResultList<TEntity>> SearchAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        int pageSize = 10, int page = 1)
    {
        var query = DbSet.AsNoTracking().AsQueryable();
        
        if (predicate != null)
            query = query.Where(predicate);
        
        var totalCount = await query.CountAsync();
        var paged = PagedResult.Create(page, pageSize, totalCount);
        
        if (orderBy != null)
            query = orderBy(query);
        
        var data = await query.Skip(paged.Skip()).Take(pageSize).ToListAsync();
        return new BaseResultList<TEntity>(data, paged);
    }
    
    // ... outros metodos
}
```

### ZetAuction.Api

Camada de apresentacao/API, ponto de entrada da aplicacao.

**Estrutura:**

```text
Configuration/
  SerilogConfig.cs
  HealthCheckConfig.cs
  ScalarConfig.cs
  SwaggerConfig.cs
  CorsConfig.cs
  ApiConfig.cs
Common/
  Api/
    IEndpoint.cs
Endpoints/
  Endpoint.cs
  Auctions/
    CreateAuctionEndpoint.cs
    GetAuctionByIdEndpoint.cs
    GetHighestBidEndpoint.cs
    PlaceBidEndpoint.cs
    SearchAuctionsEndpoint.cs
    DTOs/
      CreateAuctionRequest.cs
      PlaceBidRequest.cs
      SearchAuctionsRequest.cs
      AuctionDTOExtensions.cs
  Auth/
    LoginEndpoint.cs
    RegisterEndpoint.cs
  Bids/
    GetBidHistoryEndpoint.cs
Extensions/
  CustomExceptionHandler.cs
Middlewares/
  RequestLoggingMiddleware.cs
  PerformanceLoggingMiddleware.cs
  SecurityHeadersMiddleware.cs
  MiddlewareExtensions.cs
Program.cs
```

**Padroes:**

- **Endpoints Minimal APIs**: Classes estaticas implementando `IEndpoint` com `Map()`
- **DTOs**: Request/Response com Data Annotations + extension methods `ToCommand()` / `ToQuery()`
- **Versionamento**: `v1/` prefixo em grupos de endpoints
- **Tags**: Organizacao por dominio (`Auction`, `Bid`, `Auth`)
- **Autorizacao**: `.RequireAuthorization()` nos grupos
- **Exception Handler**: `CustomExceptionHandler` com mapeamento de excecoes para status codes
- **Middlewares**: Logging, performance, headers de seguranca
- **Scalar**: UI moderna para documentacao (substitui Swagger UI)

**Exemplo de Endpoint:**

```csharp
public class PlaceBidEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapPost("/{id:guid}/bids", HandleAsync)
            .WithName("PlaceBid")
            .Produces<BaseResult<Guid>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

    private static async Task<IResult> HandleAsync(
        [FromServices] IAmACommandProcessor commandProcessor,
        [FromServices] IBidRateLimiter rateLimiter,
        Guid id,
        [FromBody] PlaceBidRequest request)
    {
        // Rate limiting via Redis
        var allowed = await rateLimiter.IsAllowedAsync(request.UserId);
        if (!allowed)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Rate limit exceeded",
                detail: "Maximum 5 bids per 5 minutes");

        var command = request.ToCommand(id);
        await commandProcessor.SendAsync(command);

        var result = command.Result;
        
        if (result.Success)
            return TypedResults.Ok(result);
        
        return TypedResults.BadRequest(result);
    }
}
```

**Registro de Endpoints:**

```csharp
public static class Endpoint
{
    public static void MapEndpoints(this WebApplication app)
    {
        var endpoints = app.MapGroup("");

        // Health Check
        endpoints.MapGroup("/")
            .WithTags("Health Check")
            .MapGet("/health", async ([FromServices] ILogger<Program> logger) =>
            {
                logger.LogInformation("Health Check executed.");
                return Results.Ok(new { message = "OK" });
            });

        // Auth v1
        endpoints.MapGroup("v1/auth")
            .WithTags("Auth")
            .MapEndpoint<LoginEndpoint>()
            .MapEndpoint<RegisterEndpoint>();

        // Auctions v1
        endpoints.MapGroup("v1/auctions")
            .WithTags("Auction")
            .RequireAuthorization()
            .MapEndpoint<SearchAuctionsEndpoint>()
            .MapEndpoint<GetAuctionByIdEndpoint>()
            .MapEndpoint<CreateAuctionEndpoint>()
            .MapEndpoint<PlaceBidEndpoint>()
            .MapEndpoint<GetHighestBidEndpoint>()
            .MapEndpoint<GetBidHistoryEndpoint>();
    }

    private static IEndpointRouteBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder app)
        where TEndpoint : IEndpoint
    {
        TEndpoint.Map(app);
        return app;
    }
}
```

## Modelo de Dados

### Users

Entidade propria simples, sem ASP.NET Core Identity completo.

| Campo | Tipo | Observacao |
|---|---|---|
| `Id` | `Guid` | Chave primaria |
| `Login` | `string` | Unico, normalizado |
| `PasswordHash` | `string` | Hash forte (BCrypt) |
| `CreatedAtUtc` | `DateTime` | Auditoria |

### Auctions

| Campo | Tipo | Observacao |
|---|---|---|
| `Id` | `Guid` | Chave primaria |
| `CreatedByUserId` | `Guid` | Dono do leilao (FK para `Users`) |
| `Title` | `string` | Nome exibido (`name` no contrato da API) |
| `Description` | `string` | Descricao (max 5000) |
| `StartingPrice` | `decimal(18,2)` | Valor minimo inicial (`startingBid` no contrato) |
| `MinBidIncrement` | `decimal(18,2)` | Incremento minimo |
| `CurrentPrice` | `decimal(18,2)` | Atualizado por `Auction.PlaceBid` |
| `EndDate` | `timestamptz` | Encerramento em UTC (`endDateTime` no contrato) |
| `Status` | `string(20)` | `Draft`, `Active`, ou `Finalized` |
| `WinnerId` | `Guid?` | Vencedor apos `Finalize` |
| `WinningBidId` | `Guid?` | Lance vencedor |
| `WinningAmount` | `decimal(18,2)?` | Valor do lance vencedor |
| `FinalizedAtUtc` | `timestamptz?` | Momento da finalizacao |
| `RowVersion` (mapeado para `xmin`) | `xid` | Concurrency token; system column do Postgres |
| `CreatedAtUtc` | `timestamptz` | Auditoria |
| `UpdatedAtUtc` | `timestamptz` | Auditoria |
| `DeletedAtUtc` | `timestamptz?` | Soft delete |
| `IsDeleted` | `bool` | Soft delete |

### Bids

| Campo | Tipo | Observacao |
|---|---|---|
| `Id` | `Guid` | Chave primaria |
| `AuctionId` | `Guid` | Leilao |
| `UserId` | `Guid` | Usuario que ofertou |
| `Amount` | `decimal` | Valor do lance |
| `CreatedAtUtc` | `DateTime` | Momento do lance |

## Autenticacao

Autenticacao simples via **JWT Bearer**, conforme solicitado no desafio. Nao usa OAuth2 complexo nem servidor de autorizacao separado.

### Implementacao

**Registro:**

- `POST /api/v1/auth/register` - Cria conta com login unico e senha hasheada
- Senha armazenada com hash BCrypt (`PasswordHasher` em Infrastructure)
- Login normalizado para evitar duplicidade

**Login:**

- `POST /api/v1/auth/login` - Valida credenciais e emite JWT
- Payload do token contem `userId` e `login`
- Token assinado com chave simetrica (HMAC-SHA256) configurada em `appsettings.json`
- Expiracao configuravel via `Jwt:ExpirationHours` (default 1 hora)

**Protecao de endpoints:**

- Endpoints publicos: register, login, health
- Demais endpoints exigem `Authorization: Bearer {token}`
- Implementado via `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`

### Modelo de Usuario

| Campo | Tipo | Observacao |
|---|---|---|
| `Id` | `Guid` | Chave primaria |
| `Login` | `string` | Unico, normalizado |
| `PasswordHash` | `string` | Hash forte |
| `CreatedAtUtc` | `DateTime` | Auditoria |

**Nota:** Nao usa ASP.NET Core Identity completo. Apenas entidade propria com hash de senha. Isso mantem a solucao simples e atende ao requisito do desafio.

## API Proposta

### Autenticacao

- `POST /api/v1/auth/register` - Criar conta
- `POST /api/v1/auth/login` - Autenticar e obter JWT

### Leiloes

- `GET /api/v1/auctions` - Listagem com paginacao e filtro por status
- `GET /api/v1/auctions/{id}` - Detalhes do leilao
- `POST /api/v1/auctions` - Criar leilao
- `GET /api/v1/auctions/{id}/bids/highest` - Maior lance atual
- `GET /api/v1/auctions/{id}/bids` - Historico de lances (paginado)

### Lances

- `POST /api/v1/auctions/{id}/bids` - Dar lance

### Observabilidade

- `GET /health` - Health checks (Postgres + Redis)
- `GET /health/ready` - Kubernetes readiness probe
- `GET /health/live` - Kubernetes liveness probe
- `GET /health-ui` - Dashboard de health checks

## Concorrencia de Lances

O ponto mais importante da solucao e impedir que duas instancias aceitem simultaneamente lances inconsistentes.

`PlaceBid` e uma operacao transacional unica envolvida por um
pipeline Polly de retry:

1. Rate limit por usuario via Redis (`user:{userId}:bids`, sliding-window 5/300s)
2. Abrir transacao de escrita
3. Carregar leilao (xmin atual carregado pelo EF Core)
4. Validar status `Active`, `EndDate > UtcNow`, criador != bidder
5. Domain valida `amount >= CurrentPrice + MinBidIncrement`
6. Inserir o novo lance e atualizar `CurrentPrice` no aggregate
7. Aggregate emite `BidPlacedEvent` (DomainEvent)
8. `SaveChangesAsync`: o `DomainEventsSaveChangesInterceptor` mapeia o evento para um Brighter `Message` e grava em `outbox_messages` via `PostgreSqlOutbox.AddAsync`, na MESMA transacao
9. Postgres valida `xmin`: se outra transacao alterou a linha, `DbUpdateConcurrencyException` e levantada -> `ConcurrencyConflictException`
10. Polly retenta com backoff + jitter (max 3 vezes); rejeicoes de dominio (`InsufficientBidAmount`) NAO sao retentadas
11. Apos commit, `IBidCacheService.SetHighestBidAsync` faz Lua compare-and-set no Redis

Postgres usa MVCC: leituras nao bloqueiam writes. Para o caso especifico de placement de lance, a coluna de sistema `xmin` e usada como concurrency token (mapeada via `RowVersion`); writes concorrentes que viram com `xmin` antigo sao rejeitadas pelo banco com `DbUpdateConcurrencyException`, traduzidas em `ConcurrencyConflictException` e retentadas pelo pipeline Polly com backoff + jitter.

## Rate Limiting Distribuido

Implementado em **Redis** usando Sorted Sets e operacao atomica via Lua script.

**Chave:** `ratelimit:{userId}` (ZSET)

**Fluxo atomico (Lua script):**

1. Remover membros com score < `now - 5 minutos`
2. Contar membros restantes
3. Se contagem >= 5, retornar `0` (rejeitado)
4. Caso contrario, adicionar `now` ao ZSET e retornar `1` (aceito)
5. Aplicar `EXPIRE` de 5 minutos

**Vantagens:**

- Atomico: nenhuma race condition entre instancias
- Rapido: sub-milissegundo de latencia
- Auto-limpante: TTL remove eventos antigos
- Desacoplado: nao adiciona carga no Postgres

**Fallback:** Se Redis indisponivel, retornar `503 Service Unavailable`.

## Cache do Maior Lance

`GET /api/v1/auctions/{id}/bids/highest` e chamado com frequencia
(polling agressivo dos clientes).

**Estrategia (Phase 4 — ver [ADR-0004](../adr/0004-redis-highest-bid-cache.md)):**

- Redis como cache distribuido (Hash por leilao)
- Chave: `auction:{auctionId}:highest-bid`
- Atualizacao via Lua compare-and-set: `HSET` so quando o `amount`
  recebido e estritamente maior que o armazenado. Lance "menor" que
  chega tarde nao corrompe o cache
- Resposta da API ainda carrega `ETag` + `Cache-Control: private,
  max-age=1` para absorver bursts via cache HTTP
- Degradacao: se Redis cair, o `GetHighestBidQueryHandler` cai para
  query direta no banco e retorna o resultado normalmente

**Fluxo de escrita (apos lance aceito):**

1. Commit da transacao em `PlaceBidCommandHandler`
2. `IBidCacheService.SetHighestBidAsync(auctionId, bidViewModel)`
3. Lua script: `HSET ... amount=X` so se `X > stored.amount`

## Finalizacao de Leiloes

Leiloes devem ser finalizados ao atingir `EndDate`.

**Protecao dupla:**

1. Endpoint de lance rejeita leilao expirado
2. `BackgroundService` finaliza periodicamente

**Fluxo do job:**

1. Adquirir lock distribuido `finalize-auctions`
2. Buscar leiloes ativos vencidos
3. Para cada leilao:
   - Revalidar status e horario
   - Buscar maior lance
   - Definir vencedor
   - Marcar como finalizado
4. Liberar lock

## Multi-Instancia

- JWT stateless HS256
- Rate limiting via Redis
- Cache distribuido via Redis
- Transacoes Postgres para lances
- Lock distribuido para finalizacao
- Operacoes idempotentes

## Tratamento de Erros

**Excecoes de Dominio:**

- `NotFoundException` -> 404
- `ValidationException` -> 400 (com `ValidationProblemDetails`)
- `UnauthorizedAccessException` -> 401
- `ForbiddenAccessException` -> 403

**Status Codes:**

- `400` - Payload invalido
- `401` - Nao autenticado
- `403` - Criador tentando dar lance
- `404` - Recurso nao encontrado
- `409` - Lance rejeitado (valor insuficiente/concorrencia)
- `429` - Rate limit excedido
- `503` - Redis indisponivel

## Logging Estruturado

**Configuracao:**

- Serilog com leitura de `appsettings.json`
- Enrich com `FromLogContext()`
- Console para local
- Elasticsearch para centralizacao (via appsettings)

**Propriedades enriquecidas:**

- `CorrelationId` - RequestId unico
- `UserId` - Usuario autenticado
- `AuctionId` - Leilao envolvido
- `BidId` - Lance registrado
- `Endpoint` - Rota chamada
- `DurationMs` - Tempo de execucao

**Middlewares:**

- `RequestLoggingMiddleware` - Log de entrada/saida
- `PerformanceLoggingMiddleware` - Alerta de requests lentas (> 3s)

## Health Checks

**Endpoints:**

- `/health` - Status geral (JSON)
- `/health/ready` - Readiness probe (db + cache)
- `/health/live` - Liveness probe
- `/health-ui` - Dashboard visual

**Checks:**

- Postgres (via connection string)
- Redis (via connection string)

## Docker

**Dockerfile:**

- Multi-stage build
- `mcr.microsoft.com/dotnet/sdk:9.0` para build
- `mcr.microsoft.com/dotnet/aspnet:9.0` para runtime
- Porta 8080
- Health check via `/health`

**docker-compose.yml:**

- `api` - Instancia principal
- `api2` - Segunda instancia (multi-instancia)
- `redis` - Redis 7.x
- Volume compartilhado para Postgres

## Testes

### Unitarios

- Regras de dominio (entidades, validacoes)
- Handlers com mocks

### Integracao

- `WebApplicationFactory`
- Postgres in-memory ou arquivo temporario
- Redis em container de teste

**Casos:**

- Concorrencia de lances
- Rate limiting distribuido
- Cache invalidation
- Finalizacao idempotente

## Trade-offs

### Postgres + Redis

Postgres para dados transacionais, Redis para coordenacao. Separacao clara de responsabilidades.

### Rate Limiting no Redis

Operacao atomica via Lua script. Custo: infraestrutura extra. Beneficio: latencia sub-ms.

### Cache Distribuido

Consistencia entre instancias via invalidacao explicita. Fallback para banco se Redis falhar.

## Premissas

- Horarios em UTC (`IDateTimeProvider`)
- Valores monetarios em `decimal`
- JWT simples (sem ASP.NET Core Identity)
- Postgres para transacoes, Redis para coordenacao
- Soft delete por padrao

## Evolucoes

- Migrar Postgres -> PostgreSQL
- Cluster Redis para HA
- SignalR para notificacoes em tempo real
- Outbox pattern para eventos
- Auditoria completa
