# Testes de carga (k6)

Scripts [k6](https://k6.io) que exercitam os caminhos de leitura e
escrita do ZetAuction. São pensados para rodar contra a stack
docker-compose em `docker-compose.yml`.

## Scripts

| Arquivo | Propósito | SLOs |
| --- | --- | --- |
| `baseline.js` | Lê `/highest-bid` para um leilão com VU constantes. Fixa o SLO de leitura | `http_req_duration p95 < 150ms`, `p99 < 400ms`, error rate < 0.5% |
| `bid-storm.js` | Rampa para 200 RPS de placements concorrentes em um único leilão. Exercita retries de xmin, rate limiter, dispatcher do outbox | `http_req_duration p99 < 1500ms`, `accept_rate > 50%` |

## Rodando

```bash
# Sobe a stack primeiro (api em :8080, postgres, redis, observabilidade)
docker compose up -d

# SLO de leitura
k6 run tests/load/baseline.js

# Bid storm — passa um JWT emitido por /api/v1/auth/login
TOKEN=$(curl -s -X POST localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"...","password":"..."}' | jq -r '.data.token')

k6 run -e TOKEN=$TOKEN tests/load/bid-storm.js
```

## O que olhar enquanto roda

- **Grafana → ZetAuction Overview** — latência p99 de placement,
  cache hit ratio, conflitos de concorrência. O counter de conflitos
  deve subir durante a tempestade e o pipeline de retry deve manter
  accept_rate > 50%.
- **Loki** — `service.name="zetauction-api" {endpoint="place-bid"}`
  para spot-check de razões de rejeição. Uma enxurrada de
  `RateLimitHit` é normal — é o limiter fazendo seu trabalho.
- **Prometheus** —
  `process_cpu_seconds_total{service="zetauction-api"}` combinado com
  `zetauction_bids_placed_total` dá um número "lances por CPU
  segundo" útil para planejamento de capacidade.
