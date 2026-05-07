# Observabilidade

ZetAuction emite os três sinais — traces, métricas e logs — através de
um pipeline OpenTelemetry compartilhado. Todos carregam os mesmos
atributos de Resource (`service.name`, `service.version`,
`service.instance.id`), então backends conseguem pivotar entre eles.

## Topologia

```
                ┌────────────┐    OTLP gRPC :4317    ┌────────────────────┐
                │  ZetAuction│ ────────────────────▶ │  OTel Collector    │
                │     API    │                       │  (contrib 0.112)   │
                └─────┬──────┘                       └─────┬───────┬──────┘
                      │  /metrics (formato Prom)           │       │
                      │                                    │       │
                      │  scrape :15s                       │       │
                      ▼                                    ▼       ▼
                ┌────────────┐                       ┌────────┐  ┌──────┐
                │ Prometheus │ ◀───── scrape ────────│Jaeger  │  │ Loki │
                └─────┬──────┘                       └────┬───┘  └───┬──┘
                      │                                   │          │
                      └──────┬─────────────┬──────────────┘          │
                             ▼             ▼                         │
                          ┌────────────────────────┐                 │
                          │       Grafana          │ ◀───────────────┘
                          │  (datasources auto-    │
                          │   provisionadas)       │
                          └────────────────────────┘
```

A API expõe `/metrics` direto, então dashboards sobrevivem a um
outage do Collector; métricas também são empurradas via OTLP por
completude.

## Métricas custom (meter `ZetAuction.Api`)

| Métrica | Tipo | Descrição |
| --- | --- | --- |
| `zetauction.bids.placed` | counter | Lances aceitos e persistidos |
| `zetauction.bids.rejected` (tag `reason`) | counter | Lances rejeitados — `rate_limited`, `not_found`, `not_active`, `domain`, `unknown` |
| `zetauction.auctions.finalized` | counter | Leilões transitados para `Finalized` |
| `zetauction.rate_limit.hits` | counter | Requests rejeitadas pelo rate limiter de lance |
| `zetauction.db.concurrency_conflicts` | counter | Conflitos de optimistic concurrency via xmin |
| `zetauction.cache.hits` / `zetauction.cache.misses` | counter | Leituras do cache de maior lance |
| `zetauction.outbox.dispatched` / `zetauction.outbox.failed` | counter | Resultados de dispatch do outbox |
| `zetauction.bid.placement.duration` | histogram | Latência ponta-a-ponta de lances aceitos (ms) |

Instrumentações padrão de ASP.NET / HTTP / EFC / Redis / Npgsql /
runtime também ficam habilitadas —
`http_server_request_duration_seconds_*`,
`process_runtime_dotnet_gc_*`, etc.

## Correlation IDs

`CorrelationIdMiddleware` lê `X-Correlation-Id` da request e cai para
o trace id W3C `traceparent`, depois para um GUID novo. O valor
escolhido é:
1. Ecoado no header `X-Correlation-Id` da resposta.
2. Empurrado para o `LogContext` do Serilog como `CorrelationId`,
   então toda linha de log no escopo se tagueia com ele.
3. Retornado dentro de respostas RFC 7807 como a extension `traceId`.

## Stack local

```bash
docker compose up -d
```

Endpoints:

| Serviço | URL |
| --- | --- |
| API | http://localhost:8080 |
| Métricas da API | http://localhost:8080/metrics |
| Scalar API docs | http://localhost:8080/scalar/v1 |
| Healthchecks | http://localhost:8080/health |
| Jaeger UI | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 (anon Viewer; admin/admin) |
| Loki | http://localhost:3100 |
| Health do OTel Collector | http://localhost:13133 |

O dashboard "ZetAuction — Overview" no Grafana é provisionado
automaticamente a partir de
`infra/grafana/dashboards/auction-overview.json`.

## Runbooks

- [Outage do Redis](runbooks/redis-outage.md)
- [Outage do Postgres](runbooks/postgres-outage.md)
- [Spike de rejeição de lances](runbooks/bid-rejection-spike.md)
