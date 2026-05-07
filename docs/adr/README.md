# docs/adr/

Architecture Decision Records. Formato:
[MADR](https://adr.github.io/madr/).

Cada registro é imutável depois de aceito. Decisões posteriores que
sobrescrevem um registro existente fazem link à frente e o original
mantém o status `accepted` com um ponteiro `superseded by`.

A numeração é monotônica. Reuse um registro existente antes de
escrever um novo se o tópico já foi coberto.

## Índice

| # | Título | Status |
| --- | --- | --- |
| [0001](0001-postgres-primary-store.md) | Postgres como store primário | Accepted |
| [0002](0002-xmin-optimistic-concurrency.md) | `xmin` do Postgres para optimistic concurrency | Accepted |
| [0003](0003-transactional-outbox.md) | Outbox transacional para eventos de domínio | Accepted |
| [0004](0004-redis-highest-bid-cache.md) | Cache de maior lance em Redis com Lua compare-and-set | Accepted |
| [0005](0005-polly-retry-on-conflict.md) | Pipeline Polly para retry de optimistic concurrency | Accepted |
| [0006](0006-brighter-darker-cqrs.md) | Brighter + Darker para dispatch CQRS | Accepted |
| [0009](0009-rfc7807-problem-details.md) | RFC 7807 ProblemDetails como envelope de erro | Accepted |

## Formato

```markdown
# ADR-NNNN: <decisão>

- Status: Accepted | Superseded by ADR-XXXX | Deprecated
- Data: AAAA-MM-DD

## Contexto
<por que tivemos que escolher>

## Decisão
<o que escolhemos>

## Alternativas consideradas
<outras opções + por que foram rejeitadas>

## Consequências
<o que agora temos que aceitar>
```
