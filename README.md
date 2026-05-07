# ZetAuction

Serviço distribuído de leilões em .NET 9: usuários se registram,
autenticam, criam leilões, dão lances e consultam o maior lance,
enquanto o sistema garante todas as regras do assessment sob carga
multi-instância.

---

## Quickstart

```bash
docker compose up --build
```

A stack completa fica disponível em:

| Serviço | URL |
| --- | --- |
| API | http://localhost:8080 |
| Scalar API docs | http://localhost:8080/scalar/v1 |
| Health | http://localhost:8080/health |
| Métricas | http://localhost:8080/metrics |
| Grafana (anon Viewer) | http://localhost:3000 |
| Prometheus | http://localhost:9090 |
| Jaeger | http://localhost:16686 |
| Loki | http://localhost:3100 |

Parar e zerar estado:

```bash
docker compose down -v
```

Smoke test ponta-a-ponta (register → login → bid → highest →
security headers → métricas → rate limit):

```bash
bash tests/smoke/smoke.sh
```

## Como rodar os testes

```bash
# Unit
dotnet test tests/ZetAuction.UnitTests/ZetAuction.UnitTests.csproj

# Integração (Testcontainers Postgres — precisa de Docker rodando)
dotnet test tests/ZetAuction.IntegrationTests/ZetAuction.IntegrationTests.csproj

# Ou tudo
dotnet test ZetAuction.slnx
```

Mutation testing no Domain (opcional, requer
`dotnet tool install -g dotnet-stryker`):

```bash
dotnet stryker
```

Testes de carga contra a stack rodando (opcional, requer `k6`):

```bash
k6 run tests/load/baseline.js
k6 run -e TOKEN=$JWT tests/load/bid-storm.js
```

## Mapeamento ao assessment

| Requisito | Implementação |
| --- | --- |
| `POST /api/v1/auth/register` + senha hasheada | `AuthEndpoints` + BCrypt (`PasswordHasher`) |
| `POST /api/v1/auth/login` retornando credenciais | `LoginCommandHandler` emite JWT HS256; bonus sobre o "sessão também serve" do spec |
| `POST /api/v1/auctions` | Valida `endDateTime` futuro, valores positivos; leilões já saem ativos |
| `GET /api/v1/auctions` com filtro de status + paginação | Status faz bind em `AuctionStatus` enum, então um typo vira 400 tipado em vez de switch sobre string |
| `GET /api/v1/auctions/{id}` | Retorna status + maior lance; Cache-Control para absorver bursts de poll |
| `POST /api/v1/auctions/{id}/bids` | Domínio garante as quatro regras de lance; contenção concorrente é resolvida por xmin + retry Polly |
| Rate limit 5/5min sliding window, multi-instância | `RedisRateLimiterService` (Lua + ZSET) com circuit breaker e fallback in-memory |
| `GET /api/v1/auctions/{id}/bids/highest` | Cache em Redis (Lua compare-and-set) + ETag + `Cache-Control: private, max-age=1` |
| Finalização do leilão no `endDateTime` | `AuctionFinalizationWorker` roda em toda réplica e clama linhas com `FOR UPDATE SKIP LOCKED` |
| "Uma vez finalizado, não aceita mais lances" | Regra no domínio em `Auction.PlaceBid`; o status é o primeiro check |
| Testes unit + integração | 120 testes unit (incluindo property-style) + integração com Testcontainers + chaos tests de concorrência |
| README com decisões + trade-offs | Este documento, com links para os deep-dives |
| Bonus — Docker | `Dockerfile` + `docker-compose.yml` com a stack de observabilidade completa |
| Bonus — Logging estruturado | Serilog com sink OTLP; correlation id middleware em toda request |
| Bonus — Histórico paginado | `GET /api/v1/auctions/{id}/bids?page=&pageSize=` |
| Bonus — Health + métricas | `/health`, `/health/ready`, `/health/live`, `/metrics` |

Todos os endpoints ficam sob `/api/v1`. Endpoints protegidos exigem
JWT Bearer válido.

## Resumo arquitetural

A racional completa de cada decisão está nos
[ADRs](docs/adr/README.md). Um parágrafo por decisão aqui:

- **Postgres em vez de SQLite ([ADR-0001](docs/adr/0001-postgres-primary-store.md)).**
  O spec permite trocar a infraestrutura "if your design requires it".
  Correção multi-instância para lances e finalização precisa de `xmin`
  para optimistic concurrency, `FOR UPDATE SKIP LOCKED` para o worker
  de finalização e a feature `Paramore.Brighter.Outbox.PostgreSql` —
  todas dependentes de Postgres. SQLite forçaria a hand-wave a
  cláusula multi-instância.

- **Optimistic concurrency com `xmin`
  ([ADR-0002](docs/adr/0002-xmin-optimistic-concurrency.md)).** A linha do
  leilão carrega a system column `xmin` como concurrency token do
  EF Core. Conflitos viram `DbUpdateConcurrencyException`, são
  traduzidos para `ConcurrencyConflictException` e o pipeline Polly
  ([ADR-0005](docs/adr/0005-polly-retry-on-conflict.md)) refaz a tentativa
  com backoff e jitter limitados.

- **Outbox transacional via Brighter
  ([ADR-0003](docs/adr/0003-transactional-outbox.md)).** Eventos de
  domínio são drenados por um SaveChanges interceptor que mapeia cada
  `IDomainEvent` para uma `Paramore.Brighter.Message` e grava na
  tabela `outbox_messages` (schema do Brighter v9.9.13) dentro da
  mesma transação que atualiza o aggregate, via
  `EntityFrameworkPostgreSqlConnectionProvider`. O
  `BrighterOutboxDispatcherWorker` consome com
  `PostgreSqlOutbox.OutstandingMessagesAsync` e republica via
  `IAmACommandProcessor.PublishAsync`. Múltiplas réplicas exigem
  handlers idempotentes (ver ADR).

- **Cache de maior lance com Lua compare-and-set
  ([ADR-0004](docs/adr/0004-redis-highest-bid-cache.md)).** O script Lua só
  faz HSET quando o valor recebido é estritamente maior que o
  armazenado, então um lance "menor" que chega tarde não corrompe o
  cache. O spec destaca esse endpoint como alvo de polling agressivo.

- **Brighter (commands + outbox) + Darker (queries)
  ([ADR-0006](docs/adr/0006-brighter-darker-cqrs.md)).** Decisão
  afirmativa. Brighter v9.9.13 entrega tanto o bus in-process quanto
  o `PostgreSqlOutbox` (storage transacional). Os ciclos de vida de
  leitura e escrita divergem (sem domain events, sem transação em
  queries) — Darker mantém a leitura desacoplada.

- **JWT HS256 + BCrypt.** Stack de auth simples e padrão: token
  assinado com HMAC-SHA256 a partir de `Jwt:SecretKey` (mínimo 32
  caracteres validado no startup) e senhas hasheadas com BCrypt. O
  segredo é injetado por env var em todo ambiente — nunca é commitado.
  `ValidAlgorithms` é fixado em HS256 para fechar a porta a algorithm
  confusion.

- **RFC 7807 ProblemDetails em toda resposta
  ([ADR-0009](docs/adr/0009-rfc7807-problem-details.md)).** Toda resposta
  não-2xx carrega um `type` URI estável que clientes podem programar
  contra, além do trace id W3C como `traceId`.

Clean Architecture em camadas (`Shared` → `Domain` → `Application` →
`Infrastructure` → `Api`). A visão profunda — incluindo C4 e diagramas
de sequência para colocar lance, finalização, dispatch do outbox e
login — está em [`docs/architecture/`](docs/architecture/README.md).

## Trade-offs e assumptions

- **Desvio SQLite → Postgres.** Documentado acima; sem isso a história
  de correção multi-instância seria hand-wave.
- **Leilões já saem ativos.** O spec não define transição draft →
  active, então a criação vai direto para `Active`. O domínio segue
  cobrando que apenas leilões ativos aceitam lance.
- **Rate limiter falha degradado, não aberto.** Quando Redis está
  inacessível, cai para um token bucket in-memory
  ([ver runbook](docs/runbooks/redis-outage.md)). **Não** falha aberto —
  esse era o comportamento original e foi fechado de propósito.
- **JWT volta no body da resposta.** Padrão para SPAs que mantêm o
  token em memória; documentar virada para HttpOnly cookie se um dia
  navegadores embarcarem.
- **JWT HS256 + BCrypt como escolha deliberada.** Mantive a stack de
  auth simples conforme o orientado no enunciado do teste. RS256/JWKS
  + Argon2id (com forward-compat para BCrypt) são a evolução natural
  para uma versão futura — necessários quando a validação do token
  passar para um resource server externo e o atacante de senha
  começar a ser GPU-bound — mas ficaram fora desta entrega de
  propósito para não inflar o escopo.
- **`zetauction.dev/problems/...` são placeholders.** As URIs do
  ProblemDetails são estáveis no contrato (cliente pode switch sobre
  elas), mas o domínio em si ainda não hospeda a doc humana.
- **Tempo passa pelo `IDateTimeProvider`.** Toda leitura de "agora"
  passa pela abstração, então testes podem fixar tempo sem
  monkey-patch.

## Runbooks por classe de falha

| Classe | Runbook |
| --- | --- |
| Outage / degradação do Redis | [redis-outage.md](docs/runbooks/redis-outage.md) |
| Outage do Postgres | [postgres-outage.md](docs/runbooks/postgres-outage.md) |
| Spike de rejeição de lances | [bid-rejection-spike.md](docs/runbooks/bid-rejection-spike.md) |
| Mensagens travadas no outbox | [outbox-stuck-messages.md](docs/runbooks/outbox-stuck-messages.md) |

## Mapa de documentação

- [`docs/observability.md`](docs/observability.md) — topologia dos três
  sinais, catálogo de métricas custom, contrato de correlation id.
- [`docs/security.md`](docs/security.md) — tabela ameaça → mitigação →
  arquivo para auth, armazenamento de senha, brute force, headers.
- [`docs/testing.md`](docs/testing.md) — estratégia em camadas, o que
  cada camada faz e o que não é responsabilidade dela.
- [`docs/architecture/`](docs/architecture/README.md) — modelo C4 +
  fluxos em sequence diagrams.
- [`docs/adr/`](docs/adr/README.md) — conjunto completo de ADRs.

## Configuração

| Variável | Função |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Connection string do Postgres |
| `ConnectionStrings__Redis` | Endpoint do Redis |
| `Jwt__Issuer` / `Jwt__Audience` | Parâmetros de validação do JWT |
| `Jwt__ExpirationHours` | Tempo de vida do token (default 1h) |
| `Jwt__SecretKey` | Segredo HMAC para assinar JWTs (mínimo 32 caracteres; inject via secret manager em produção) |
| `Otel__ServiceName` | Nome do recurso para o OpenTelemetry |
| `Otel__OtlpEndpoint` | Endpoint OTLP gRPC do OTel Collector |

## Estrutura do projeto

```text
ZetAuction/
├── src/
│   ├── ZetAuction.Shared/         primitives, BaseResult, IDateTimeProvider
│   ├── ZetAuction.Domain/         aggregates, eventos, exceções, contratos de repositório
│   ├── ZetAuction.Application/    commands, queries, handlers, view models
│   ├── ZetAuction.Infrastructure/ EF Core, Postgres, Redis, JWT, BCrypt, repositórios
│   └── ZetAuction.Api/            Minimal APIs, middlewares, workers, OTel wiring
├── tests/
│   ├── ZetAuction.UnitTests/      testes de domínio + handlers, suite property-style
│   ├── ZetAuction.IntegrationTests/ Testcontainers + chaos tests
│   ├── load/                      scripts k6 baseline + bid-storm
│   └── smoke/                     driver shell ponta-a-ponta
├── infra/                         configs Prometheus, Grafana, Loki, OTel Collector
├── chart/zetauction/              Helm chart (formato de deploy de produção)
├── docs/                          ADRs, C4, sequence diagrams, runbooks
├── docker-compose.yml             stack local (API + observabilidade)
├── Dockerfile                     multi-stage, non-root, runtime alpine
└── stryker-config.json            config de mutation testing
```

## Licença

MIT
