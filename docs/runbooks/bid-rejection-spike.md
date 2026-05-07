# Runbook: Spike de rejeição de lances

**Owner:** Auctions team
**Severity floor:** SEV-3 (issue de UX, sem perda de dados)
**Severity ceiling:** SEV-2 se rejeições correlacionarem com
conflitos de concorrência > 1/s (provavelmente um stampede de hot-key
de leilão que precisa de partition).

## O que você já deveria ver

| Sinal | Onde | Threshold |
| --- | --- | --- |
| Spike na rate de `zetauction_bids_rejected_total` | Grafana → ZetAuction Overview → "Bids rejected" | > 0.1/s sustentado |
| `zetauction_db_concurrency_conflicts_total` subindo | Mesmo dashboard, painel "Concurrency conflicts" | > 25/5min |
| Rate de 422 / 409 subindo | Grafana → "HTTP request rate by route" filtrado em `POST /api/v1/auctions/{id}/bids` | fração 4xx não-trivial |

## Triage

Uma rejeição *não* é um bug — regras de domínio legitimamente
rejeitam:
- Lance abaixo do maior lance atual (`InsufficientBidAmountException` → 422)
- Leilão não está em status `Active` (`AuctionClosedException` → 409)
- Falha de validação (`InvalidBidException` → 422)
- Conflito de concorrência após retry esgotado
  (`ConcurrencyConflictException` → 409)

O job aqui é descobrir *qual classe* domina. Passos:

1. Puxa logs recentes escopados em um leilão problemático:
   ```
   service.name="zetauction-api" {request_path="/api/v1/auctions/<id>/bids"} | json | level="Warning" or level="Error"
   ```
   No Grafana → Loki, essa query acende a classe de rejeição via
   o tipo da exception que o handler do ProblemDetails logou.

2. Cross-reference com traces no Jaeger (service `zetauction-api`,
   operation `POST /api/v1/auctions/{auctionId}/bids`). O span
   contém `db.statement` para a chamada EF e `exception.type` para
   a rejeição.

3. Bucket por classe:
   - **Maioria `InsufficientBidAmount`**: competição legítima.
     Continue observando; se um leilão popular está gerando milhares
     desses, considere se a janela do rate limiter está apropriada
     (é apertada de propósito para prevenir griefing).
   - **Maioria `AuctionClosed`**: clientes estão correndo contra a
     janela de fechamento. Investiga `EndDate` do leilão vs skew de
     relógio do cliente. Vale checar se o finalizer não está
     atrasando (painel "Auctions finalized" do Grafana; deve rodar
     dentro de 5s do `EndDate`).
   - **Maioria `ConcurrencyConflict`**: contenção em hot-row. O
     retry Polly queima 3 tentativas antes de desistir; uma rate
     sustentada de `ConcurrencyConflict` significa que escritas
     estão colidindo mais rápido do que o retry consegue resolver.
     Sharding por id de leilão, aumentar contagem de retry, ou
     pre-validar contra o cache Redis antes de bater no DB são
     opções. **Não** alargue a janela de retry além de ~100ms —
     isso aumenta tail latency para todo mundo.
   - **Maioria `InvalidBid`**: cliente ruim. Verifica o user-agent.

## Mitigação

Raramente há um fix server-side durante o incidente em si; as
regras de domínio estão certas e a proteção é intencional.
Alavancas de curto prazo úteis:

- Reduzir o *budget* do rate limit se um único usuário é responsável
  pelo spike (configurável por leilão no limiter).
- Pausar o leilão (`PATCH /api/v1/auctions/{id}/cancel`) só como
  último recurso e depois de aprovação do operador — isso é
  visível ao usuário.

## O que capturar para o post-mortem

- Um breakdown de classes de rejeição durante a janela de spike. A
  query Loki acima vai dar.
- Os top 3 IDs de leilão por contagem de rejeição.
- Se o spike correlaciona com o painel `RateLimitHits` — se sim, o
  limiter fez seu trabalho; se não, pode ser um atacante / botnet
  mais esperto (ver `redis-outage.md` para o caveat do fallback
  in-memory).
