# C2 — Visão de container

Dá um zoom de um nível dentro da fronteira do sistema ZetAuction.
Cada caixa é uma unidade implantável; setas são protocolos de rede.

```mermaid
C4Container
    title ZetAuction — Container View

    Person(bidder, "Bidder")
    Person(operator, "Operator")

    System_Boundary(zb, "ZetAuction") {
        Container(api, "ZetAuction API", ".NET 9 / ASP.NET Core", "Minimal APIs sob /api/v1, JWT auth, hospeda o dispatcher do outbox e os workers de finalizer.")
        ContainerDb(postgres, "Postgres 16", "RDBMS", "Estado dos aggregates, outbox transacional, migrations EF Core.")
        ContainerDb(redis, "Redis 7", "Key-value", "Cache de maior lance (Lua compare-and-set), rate limiter de lance (sliding window).")
        Container(otelc, "OTel Collector", "Container", "Faz fan-out de traces/métricas/logs OTLP para Jaeger, Prometheus, Loki.")
        Container(jaeger, "Jaeger", "Container", "Storage e UI de traces.")
        Container(prom, "Prometheus", "Container", "Storage de métricas, scrape em /metrics na API + collector.")
        Container(loki, "Loki", "Container", "Storage de logs, ingestão via OTLP.")
        Container(grafana, "Grafana", "Container", "Provisionamento de datasources + dashboard ZetAuction overview.")
    }

    Rel(bidder, api, "REST + JWT", "HTTPS / 8080")
    Rel(operator, grafana, "Dashboards", "HTTPS / 3000")

    Rel(api, postgres, "EF Core 9 + xmin", "TCP / 5432")
    Rel(api, redis, "StackExchange.Redis", "TCP / 6379")
    Rel(api, otelc, "OTLP", "gRPC / 4317")
    Rel(prom, api, "scrape /metrics", "HTTP / 8080")
    Rel(otelc, jaeger, "OTLP", "gRPC")
    Rel(otelc, prom, "Formato Prom", "HTTP / 8889")
    Rel(otelc, loki, "OTLP-HTTP", "HTTP / 3100")
    Rel(grafana, prom, "PromQL", "HTTP")
    Rel(grafana, loki, "LogQL", "HTTP")
    Rel(grafana, jaeger, "Trace fetch", "HTTP")
```

## O que mora onde

| Container | Construído de | Responsabilidades-chave |
| --- | --- | --- |
| **API** | `Dockerfile` | Superfície HTTP, dispatch de command/query, worker do dispatcher de outbox, worker do finalizer de leilão, emissores de observabilidade |
| **Postgres** | `postgres:16-alpine` | Fonte da verdade para aggregates e tabela do outbox |
| **Redis** | `redis:7-alpine` | Cache hot de leitura + rate limiter distribuído |
| **OTel Collector** | `otel/opentelemetry-collector-contrib` | Fan-out único para traces / métricas / logs |
| **Prometheus** | `prom/prometheus` | Scrape + storage. `/metrics` também é exposto direto pela API, então dashboards sobrevivem a um outage do Collector |
| **Loki** | `grafana/loki` | Logs |
| **Jaeger** | `jaegertracing/all-in-one` | Traces |
| **Grafana** | `grafana/grafana` | Dashboards (auto-provisionados de `infra/grafana/`) |

## Por que um único container API, não três

A API hospeda três responsabilidades (HTTP, dispatcher do outbox,
finalizer de leilão) dentro de um único processo. Poderíamos
separar, e num deployment muito maior nós separaríamos. Na escala de
hoje:

- Os três precisam do mesmo data context (EF Core + outbox).
- `FOR UPDATE SKIP LOCKED` (ADR-0003, finalizer) torna os workers
  seguros para rodar em toda réplica sem coordenação.
- Separar introduz superfície de deploy adicional sem ganho mensurável
  de correção ou performance.

Separar fica interessante quando bater em *qualquer um* destes:

- Latência de dispatch do outbox começar a competir com o budget de
  request da API.
- O finalizer precisar de characteristics de scaling diferentes do
  caminho HTTP (ex.: backlog após um outage).

O Helm chart (`chart/zetauction/`) é estruturado para conseguirmos
esculpir um Deployment separado para qualquer dos workers sem
re-conectar o runtime.
