# Runbook: Outage / degradação do Postgres

**Owner:** Platform team
**Severity floor:** SEV-1 (escritas falham; o leilão fica
inutilizável para lances).

## O que você já deveria ver

| Sinal | Onde | Threshold |
| --- | --- | --- |
| Healthcheck do Postgres vermelho | Healthchecks UI / `/health` | `npgsql: Unhealthy` |
| Spike de 5xx em `POST /api/v1/auctions/{id}/bids` | Grafana → "HTTP request rate by route" invertido com status | sustentado > 1% |
| `zetauction_db_concurrency_conflicts_total` chato | Stat panel do Grafana | silêncio incomum após tráfego sustentado também indica que escritas estão falhando antes |
| Lag do outbox crescendo | Grafana "Outbox dispatch rate" + contagem no DB | `SELECT count(*) FROM outbox_messages WHERE "dispatched" IS NULL` subindo |

## Comportamento por componente

- **API** retorna RFC 7807 `503 Service Unavailable` (ou `500` para
  o caso de borda em que a conexão cai no meio da transação). Todas
  as escritas de lance falham. O wrapper Polly retry dentro do
  `PlaceBidCommandHandler` *não* retenta falhas de classe
  conexão — só conflitos de optimistic concurrency.
- **AuctionFinalizationWorker** para de clamar linhas. Sem avanço
  de estado. Quando Postgres voltar, o worker resume de onde parou
  via `FOR UPDATE SKIP LOCKED`.
- **BrighterOutboxDispatcherWorker** para de drenar (
  `PostgreSqlOutbox.OutstandingMessagesAsync` lança). Mensagens
  permanecem duráveis na tabela `outbox_messages`; nenhum evento
  é perdido. Quando Postgres volta, dispatch resume.

## Mitigações (em ordem)

1. **Confirme escopo.** `psql` de um sidecar / bastion. Se
   inacessível, pula para o passo 2.
2. **Restart do Postgres.** Local: `docker compose restart postgres`.
   Produção: dispara o failover do DB gerenciado; **não** force um
   swap de primary a menos que replicação esteja verificada.
3. **Drena réplicas.** Se a issue é saturação de conexão, escala
   réplicas da API para baixo antes do Postgres voltar para evitar
   uma tempestade de reconexão.
4. **Recupera o outbox.** Uma vez que escritas estão saudáveis, o
   dispatcher dá catch-up automaticamente. Observe a rate de
   `zetauction_outbox_dispatched` subindo. Se
   `zetauction_outbox_failed` persiste, inspeciona as linhas
   outstanding diretamente (Brighter v9.9.13 não persiste
   `FailureCount` — falha aparece nos logs do worker e no contador):
   ```sql
   SELECT "messageid",
          "topic",
          "timestamp",
          "headerbag"::jsonb ->> 'clr_type' AS clr_type
   FROM outbox_messages
   WHERE "dispatched" IS NULL
   ORDER BY "timestamp"
   LIMIT 50;
   ```

## Migrations durante incidente

**Não** rode `dotnet ef database update` ou faça restart do job
`api-migrate` no meio de incidente. EF Core adquire um advisory
lock por migration; uma migration parcialmente aplicada durante um
flap do Postgres pode te deixar num estado em que
`__EFMigrationsHistory` diz que X foi aplicada mas o schema
discorda. Espere health verde estável e inspecione
`SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC`
antes de qualquer trabalho de schema.

## Por que não estamos auto-fail-open

Aceitação de lance é o produto inteiro. Não existe modo degradado
em que "aceita o lance só no Redis" seja aceitável — esse caminho
criou o bug que a Phase 4 fechou (ordering do write-through no
cache). Falhar fechado é intencional.
