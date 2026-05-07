# Runbook: Mensagens travadas no outbox

**Owner:** Platform team
**Severity floor:** SEV-3 (consumers downstream veem eventos
atrasados)
**Severity ceiling:** SEV-2 se o outbox cresce sem limite —
eventualmente isso vira incidente de pressão de disco no Postgres.

> Outbox é o nativo do `Paramore.Brighter.Outbox.PostgreSql`
> v9.9.13. Tabela: `outbox_messages` (lowercase, sem aspas).
> Schema gerenciado pelo Brighter — colunas chave: `Id` (BIGSERIAL),
> `MessageId` (UUID UNIQUE), `Topic` (VARCHAR), `MessageType`
> (VARCHAR — enum do Brighter: `MT_EVENT`, `MT_COMMAND`, …),
> `Timestamp` (timestamptz), `Dispatched` (timestamptz NULL),
> `HeaderBag` (TEXT JSON — carrega `clr_type` / `aggregate_type` /
> `aggregate_id`) e `Body` (TEXT JSON do evento). Ver ADR-0003.

## O que você já deveria ver

| Sinal | Onde | Threshold |
| --- | --- | --- |
| Rate de `zetauction_outbox_failed` subindo | Grafana → ZetAuction Overview → "Outbox dispatch rate" | failed > dispatched por >5min |
| Contagem de linhas outstanding subindo | Query direta no Postgres | `> 1000` linhas com `Dispatched IS NULL` |
| Logs do worker cheios de `Outbox dispatch failed for message` | Loki | warnings sustentados chaveados pelo mesmo `Topic` |

Query direta para o backlog outstanding:

```sql
SELECT count(*) AS pending,
       max(now() - "timestamp") AS oldest_age
FROM outbox_messages
WHERE "dispatched" IS NULL;
```

(Brighter v9.9.13 não persiste `FailureCount` / `LastErrorMessage`
no schema — falhas são visíveis apenas pelos logs do worker e pelo
contador `zetauction_outbox_failed`.)

## Triage

O `BrighterOutboxDispatcherWorker` falha uma mensagem individual
sob três classes:

1. **Falha de resolução de tipo.** `Type.GetType(clr_type)` retornou
   null. Causa: skew de deployment — o worker está num assembly mais
   antigo que o writer que produziu a linha, ou um tipo de evento
   foi renomeado sem migration. A exception é
   `InvalidOperationException("Cannot resolve message type ...")`.
2. **Falha de deserialização.** Drift de schema do payload entre
   writer e reader. A exception é `JsonException`.
3. **Falha do handler subscriber.** Brighter despachou via
   `PublishAsync<T>`, o `RequestHandlerAsync<T>` correspondente
   lançou. A exception é o que quer que o handler tenha levantado.

Bucket por `Topic` (FQN do tipo .NET serve como topic do Brighter):

```sql
SELECT "topic",
       count(*) AS outstanding,
       max(now() - "timestamp") AS oldest_age
FROM outbox_messages
WHERE "dispatched" IS NULL
GROUP BY "topic"
ORDER BY count(*) DESC;
```

Para inspecionar payload + clr_type de uma linha específica:

```sql
SELECT "messageid", "topic", "timestamp",
       "headerbag"::jsonb ->> 'clr_type' AS clr_type,
       "headerbag"::jsonb ->> 'aggregate_id' AS aggregate_id,
       "body"
FROM outbox_messages
WHERE "messageid" = '...';
```

## Mitigações

### Classe 1: resolução de tipo

O fix é sempre "deploya o assembly que contém o tipo" — não tem
atalho seguro. **Não** delete as linhas; uma vez que a versão
correta entrou em produção, o worker dá catch-up automaticamente
(linha continua outstanding e é repolada).

Se o rename foi deliberado, fornece uma migration SQL one-shot que
reescreve o `clr_type` dentro do `HeaderBag`:

```sql
UPDATE outbox_messages
SET "headerbag" = jsonb_set(
        "headerbag"::jsonb,
        '{clr_type}',
        '"New.Namespace.NewName, NewAssembly"'::jsonb
    )::text
WHERE "dispatched" IS NULL
  AND "headerbag"::jsonb ->> 'clr_type' = 'Old.Namespace.OldName, OldAssembly';
```

### Classe 2: deserialização

Mesma forma: ou rollback o writer que quebrou o schema, ou
rollforward o reader. Evite edição in-place de `Body` — JSON
auditável e modificá-lo quebra a trilha de auditoria.

### Classe 3: falha de handler

Bug no subscriber. Brighter v9.9.13 não tem backoff por linha — a
linha permanece outstanding e o worker repete na próxima iteração
(default 500ms). Conserta o handler e deploya. Se a falha é
sustentada e ruidosa, encurte o `Topic` para silenciar enquanto
investiga (`UPDATE ... SET dispatched = now() WHERE topic = ...`)
— mas só depois de copiar para a dead-letter (próxima seção).

## Quando desistir de uma mensagem

Se uma mensagem ficou outstanding por mais de 24h e o modo de
falha não é transient, marca como dispatched manualmente depois de
copiar a linha para uma tabela dead-letter:

```sql
INSERT INTO outbox_messages_deadletter SELECT * FROM outbox_messages WHERE "messageid" = '...';
UPDATE outbox_messages SET "dispatched" = now() WHERE "messageid" = '...';
```

(`outbox_messages_deadletter` é gerenciada pelo operador; cria sob
demanda. Schema espelha `outbox_messages`.)

Esse é um passo deliberado humans-in-the-loop. Não automatize.

## Por que não tem auto-DLQ hoje

Auto-DLQ é conceitualmente simples mas introduz um failure mode
silencioso — eventos saem da view do dispatcher, e a menos que o
operador observe crescimento de DLQ, o sistema parece saudável.
Hoje o counter de falhas e o alerta do dashboard são o sinal mais
ruidoso. Reavaliar quando/se o catálogo de subscribers crescer
além do trivial.

## Idempotência: por que múltiplas réplicas não duplicam side-effects

Brighter v9.9.13 **não usa `FOR UPDATE SKIP LOCKED`** —
`OutstandingMessagesAsync` é um SELECT simples. Réplicas
concorrentes podem ler a mesma mensagem antes do
`MarkDispatchedAsync` commit. Para fechar essa janela, o
`BrighterOutboxDispatcherWorker` pré-reivindica o `MessageId` na
tabela `processed_events` (PRIMARY KEY em `event_id`) via
`INSERT ... ON CONFLICT DO NOTHING` **antes** de chamar
`PublishAsync<T>`. Apenas a réplica que ganha o INSERT roda o
handler chain; a perdedora pula publish e ainda assim chama
`MarkDispatchedAsync` (a linha sai do polling em todas as
réplicas). Isso garante "handler at most once per event" sem
exigir que cada subscriber implemente sua própria deduplicação.

Útil quando algo parece duplicado:

```sql
SELECT count(*) AS deduped FROM processed_events;
SELECT event_id, processed_at
FROM processed_events
ORDER BY processed_at DESC
LIMIT 20;
```

Métrica relacionada: `zetauction_outbox_duplicate_skipped_total`
(quantas vezes a réplica perdeu o claim — sustained não-zero
sinaliza ou over-replication ou poll interval menor que a latência
de dispatch).

## Poison-message: dead-lettering automático

Mensagens permanentemente falhas (JSON corrompido, `clr_type`
ausente do assembly, handler bug) não fazem mais infinite-loop. O
worker mantém failure count em memória; após **10 tentativas** a
linha é forçada para `Dispatched` e a métrica
`zetauction_outbox_dead_lettered_total` incrementa — qualquer valor
não-zero deveria gerar alerta. A linha **permanece** em
`outbox_messages` (apenas com `Dispatched` populado), preservando o
payload para triagem:

```sql
-- Mensagens que foram dead-lettered (Dispatched preenchido + log
-- correlato com "exceeded 10 dispatch attempts" no Loki).
SELECT messageid, topic, dispatched,
       headerbag::jsonb ->> 'clr_type' AS clr_type
FROM outbox_messages
WHERE dispatched IS NOT NULL
ORDER BY dispatched DESC
LIMIT 50;
```

Cruze o `messageid` com os logs do `BrighterOutboxDispatcherWorker`
no Loki (filter `MessageId = '...'`) para encontrar a stack trace
da última falha que disparou o dead-letter.
