# Estratégia de testes

Os testes do ZetAuction são em camadas para dar cobertura defensável
em cada seam, sem deixar uma camada virar substituta das outras.

## Unit tests — `tests/ZetAuction.UnitTests`

Pure-CLR, sem I/O. Dois sabores moram aqui:

- **Testes Fact / Theory** (`AuctionTests.cs`, etc.) fixam
  comportamento para casos a dedo. Lê primeiro pra entender o
  domínio.
- **Property-style tests** (`AuctionPropertyTests.cs`) dirigem o
  Auction aggregate com streams de input pseudo-aleatórios com seed.
  Cada propriedade nomeia uma invariante — monotonicidade do
  `CurrentPrice`, um-evento-por-bid-aceito, semântica de fechamento
  "highest amount wins" — que precisa valer para qualquer sequência
  de lances admissível. A seed é parte do argumento do teste, então
  quando uma propriedade regride, a seed que falha sobrevive ao
  triage de teste flaky.

Rodar: `dotnet test tests/ZetAuction.UnitTests`

## Mutation tests — Stryker.NET

Pega a classe de bug onde um teste passa contra qualquer
implementação (ex.: `>` virou `>=`, `&&` virou `||`). Roda local
para validar a robustez dos unit tests; CI roda o mesmo no nightly
com threshold imposto.

```bash
dotnet tool install -g dotnet-stryker
dotnet stryker
```

Configuração mora em `stryker-config.json`. Targets de mutation
ficam restritos à camada Domain (aggregates, value objects, regras
de negócio) — código instrumentation-heavy (eventos, exceções,
validators) é excluído então o score reflete mutações de lógica.

Thresholds:
- High: 80%
- Low: 70%
- Break: 60% (CI falha abaixo disso)

## Integration tests — `tests/ZetAuction.IntegrationTests`

Cada classe de teste sobe um container ephemeral do Postgres 16 via
Testcontainers (sem SQLite, sem in-memory provider) e roda a API
através de um banco isolado por Respawn. Categorias:

- **Endpoint tests** — contrato request/response para cada rota sob
  `/api/v1`. Exercitam roteamento, auth, formato de ProblemDetails e
  paginação. O TestRateLimiter dobra para o limiter Redis então
  esses testes não precisam de Redis real.
- **Chaos tests** (`ConcurrencyChaosTests`) — cenários adversariais:
  lances concorrentes em um único leilão, burst contra o rate
  limiter, consistência cache-vs-DB sob intercalamento de
  leitura/escrita. Eles verificam as partes móveis que os endpoint
  tests não conseguem alcançar.

Rodar: `dotnet test tests/ZetAuction.IntegrationTests`

## Load tests — `tests/load/` (k6)

Dois perfis, ambos pensados para rodar contra a stack
docker-compose:

- `baseline.js` fixa o SLO de leitura para `/highest-bid`.
- `bid-storm.js` rampa para 200 RPS de placements concorrentes.

Ver `tests/load/README.md` para detalhes. Não fazem parte do
`dotnet test` — emendar no nightly do CI quando o time tiver apetite
para o tempo extra de execução.

## O que NÃO está coberto (ainda)

- Mutation testing contra a camada Application. Stryker está
  escopado em Domain porque os testes de handler usam Moq, que
  produz survivors espúrios. O tier Application mantém cobertura
  unit comum.
- Testes de aceitação estilo BDD — não tem camada Gherkin, por
  design. Os endpoint tests já dobram como documentação viva.
- Injeção de falha cross-region — os runbooks documentam o que
  aconteceria mas o projeto (ainda) não faz deploy multi-region.
