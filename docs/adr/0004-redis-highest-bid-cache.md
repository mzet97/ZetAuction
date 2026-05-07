# ADR-0004: Cache de maior lance em Redis com Lua compare-and-set

- Status: Accepted
- Data: 2026-05-07

## Contexto

O caminho de leitura `/highest-bid` é o endpoint mais movimentado do
sistema — todo leilão ativo dispara um poll da UI, todo lance é
seguido por uma leitura de confirmação. Bater no Postgres a cada
chamada faz o banco virar gargalo e dá ao bidder uma latência de
pior caso dominada pelo round trip do EF Core.

Também descobrimos um bug de ordenação no write-through: dois lances
concorrentes em valores A e B (A < B) podem acabar com o menor valor
no Redis se as threads se intercalam como `commit(A) → cache.set(A)`
depois de `commit(B) → cache.set(B)`.

## Decisão

Cachear o maior lance corrente por leilão em Redis sob um hash
guardando tanto o amount quanto o payload serializado. As escritas
passam por um script Lua que faz um compare-and-set atômico:

```
if redis.call("HGET", KEYS[1], "amount") < ARGV[1] then
  redis.call("HSET", KEYS[1], "amount", ARGV[1], "payload", ARGV[2])
end
```

Isso torna o cache monotonicamente não-decrescente em `amount`, então
escritas "menores" que chegam tarde são descartadas em vez de
sobrescrever o estado corrente.

## Alternativas consideradas

- **Só invalidação de cache.** Rejeitada. O hot path de lance
  produz uma sequência constante de invalidações seguidas de leituras,
  o que vira thundering herd no Postgres.
- **Lock distribuído ao redor do write.** Rejeitada. Adiciona
  latência a todo lance aceito por um problema que o Lua resolve em
  um único round trip de rede.
- **Atualização do cache via Pub/Sub.** Considerada e adiada. O
  worker de outbox publica eventos de qualquer jeito; a gente
  poderia se inscrever e atualizar o cache a partir daí. A abordagem
  Lua-com-comparação é mais simples e já é correta sob contenção.

## Consequências

- Tanto leituras quanto escritas no cache pagam um Lua. Lua no Redis
  é single-thread e rápido (<1ms p99 na prática).
- Outage de Redis nos coloca num modo fail-degraded — ver ADR-0005 e
  `docs/runbooks/redis-outage.md`.
- O cache pode ficar atrasado em relação ao banco em exatamente um
  lance aceito-mas-não-settled. Isso é aceitável para a UI do bidder;
  o estado autoritativo é sempre Postgres.
