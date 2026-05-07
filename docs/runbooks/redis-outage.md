# Runbook: Outage / degradação do Redis

**Owner:** Platform team
**Severity floor:** SEV-2 (latência de escrita degradada, rate limit
fail-open)
**Severity ceiling:** SEV-1 (respostas de maior lance servem dados
stale apenas — sem perda de dados, mas a UX do bidder sofre)

## O que você já deveria ver

| Sinal | Onde | Threshold |
| --- | --- | --- |
| Spike na rate de `zetauction_cache_misses_total` | Grafana → ZetAuction Overview → "Cache hit ratio" | hit ratio < 50% por 5min |
| Polly circuit breaker aberto | Logs da API | warning de `RedisRateLimiterService` "Rate limiter circuit-broken; falling back…" |
| Healthcheck do Redis vermelho | Endpoint `/health`, Healthchecks UI | `redis: Unhealthy` |
| Latência em `POST /api/v1/auctions/{id}/bids` | Dashboard "Bid placement latency p95/p99" | p99 > 250ms |

## O que ainda funciona

- **Placement de lance** continua. A escrita no cache é best-effort;
  o estado autoritativo mora em Postgres. Pior caso: leituras
  subsequentes de `GET /highest-bid` servem um valor um pouco antigo
  até o cache se recuperar.
- **Rate limiting** cai para o limiter in-memory
  (`InMemoryRateLimiter`, budget default 2 / janela). É por-processo
  e *não* compartilhado entre réplicas, então o cap global fica
  loosely (replicas × 2) até o Redis voltar.
- **Finalização de leilão** não é afetada — não toca em Redis.

## O que para de funcionar

- O primeiro `GET /highest-bid` após uma escrita pode ler valor
  stale até a próxima escrita bem-sucedida reidratar o cache.
- Respostas 304 baseadas em ETag podem produzir thundering herd no
  Postgres se o Redis ficar fora por uma janela longa. Observe
  `pg_stat_activity` para saturação de conexão.

## Mitigações (em ordem)

1. **Confirme o escopo.** Bata `redis-cli -h <host> ping` de dentro
   do pod / container da API. Se inacessível, pula para o passo 2.
   Se acessível mas lento (`SLOWLOG get 10`), inspecione a query
   lenta e considere o passo 3.
2. **Restart do Redis.** `docker compose restart redis` (local) ou o
   equivalente da sua orquestração. A API reconecta via wrapper
   Polly retry sem restart.
3. **Limita a pressão de memória.** `redis-cli INFO memory`; se
   `used_memory_rss` está perto de `maxmemory`, a eviction LRU está
   thrashando. Sobe o `--maxmemory` ou shedda carga (reduz contagem
   de réplica da API) até conseguir escalar Redis.
4. **Se Redis ficar fora >5 min**, considera escalar a API para
   uma única réplica para manter o limiter in-memory consistente.
   Isso troca throughput por enforcement previsível do rate.

## Recuperação

- Depois que Redis volta, você **não** precisa reconstruir o cache
  de maior lance manualmente. A primeira leitura após uma escrita
  bem-sucedida vai re-popular o hash via o script Lua de
  compare-and-set.
- O circuit breaker Polly fecha na próxima chamada bem-sucedida; sem
  reset manual.

## O que capturar para o post-mortem

- Timestamps de início / fim do Grafana (os stat panels para cache
  hit ratio + rejeições do rate limiter ambos pinam a janela).
- Output de `redis-cli INFO` tirado antes da mitigação (memória,
  clientes conectados, chaves evicted).
- Número de deltas de `zetauction_rate_limit_hits_total` durante a
  janela — útil para estimar quanto de tráfego real bateu no
  fallback.

## Por que isso é fail-degraded, não fail-open

A implementação original silenciosamente caía aberta quando o Redis
era inacessível, o que significava que um outage do Redis também
virava enxurrada não-medida de bidding. A Phase 4 introduziu o
circuit breaker Polly + fallback in-memory para um outage degradar
graciosamente em vez de remover proteção.
