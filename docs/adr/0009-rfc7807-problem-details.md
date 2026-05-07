# ADR-0009: RFC 7807 ProblemDetails como envelope de erro

- Status: Accepted
- Data: 2026-05-07

## Contexto

A API original retornava erros como uma mistura de strings nuas,
formatos JSON ad-hoc e respostas `BadRequest` sem informação de tipo
machine-readable. Isso impossibilitava clientes programarem contra
classes específicas de falha (ex.: "auction is closed" vs
"insufficient amount") sem fazer parse de mensagem em inglês.

## Decisão

Toda resposta não-2xx carrega um body
`application/problem+json` RFC 7807 produzido pelo
`ProblemDetailsExceptionHandler`:

- `type` é uma URI estável sob
  `https://zetauction.dev/problems/<slug>` (ex.: `auction-closed`,
  `concurrency-conflict`, `insufficient-bid-amount`). Clientes podem
  programar contra a URI.
- `title` é curto, humanamente legível, estável.
- `status` corresponde ao status HTTP.
- `detail` é o específico humanamente legível da ocorrência atual e
  não faz parte do contrato.
- `instance` é o path da request.
- As extensões `traceId` e `activityId` carregam o trace id W3C e o
  Activity id .NET para um relatório do cliente poder ser
  cross-referenced contra traces no Jaeger.

Mappings (ver `ProblemDetailsExceptionHandler` para a fonte da
verdade):

| Exception | HTTP | Type slug |
| --- | --- | --- |
| `InsufficientBidAmountException` | 422 | `insufficient-bid-amount` |
| `InvalidBidException` | 422 | `invalid-bid` |
| `AuctionClosedException` | 409 | `auction-closed` |
| `ConcurrencyConflictException` | 409 | `concurrency-conflict` |
| `DomainException` (catch-all) | 422 | `domain-error` |
| Rate-limit rejection | 429 | `rate-limited` |

## Alternativas consideradas

- **Envelope feito sob medida.** Rejeitada. RFC 7807 é o padrão da
  indústria; ASP.NET Core tem suporte de primeira classe; clientes
  ganham tooling de graça.
- **Retornar array "errors" estilo GraphQL.** Rejeitada. Não somos
  GraphQL. Modelar erros de duas formas cria ambiguidade.

## Consequências

- Clientes que programam contra classes de rejeição de domínio podem
  fazer switch sobre a URI `type` em vez de fazer parse de string.
- Adicionar uma nova domain exception exige adicionar o mapping no
  `ProblemDetailsExceptionHandler` e (idealmente) um link público
  descrevendo o tipo. Hoje as URIs dão 404 — ficam reservadas para
  referência humana.
- A extension `traceId` é o mesmo id usado no header
  `X-Correlation-Id` da resposta, então um único id reproduz o
  problema em logs, traces e dashboards de métricas.
