# ADR-0001: Postgres como store primário

- Status: Accepted
- Data: 2026-05-07

## Contexto

O assessment original veio com SQLite como store relacional. Isso
deixava o ciclo de desenvolvimento local rápido, mas nos travava de
usar features específicas do Postgres que o domínio de leilão de fato
precisa: optimistic concurrency via `xmin`, `FOR UPDATE SKIP LOCKED`,
partial indexes, colunas JSONB para o outbox e advisory locks para o
runner de migrations do EF Core.

Também precisávamos de uma história para correção multi-instância
desde o início — o finalizer de leilão e o dispatcher do outbox
rodam em toda réplica e não podem processar duas vezes. A ausência de
locks em nível de linha no SQLite tornava isso difícil na melhor das
hipóteses.

## Decisão

Postgres 16 é o único store primário suportado. O projeto Infrastructure
fixa `Npgsql.EntityFrameworkCore.PostgreSQL` e registra o mapeamento de
`xmin` incondicionalmente.

Desenvolvimento local usa a imagem `postgres:16-alpine` via
docker-compose; testes de integração usam a mesma imagem via
Testcontainers; produção roda contra um Postgres gerenciado (ou
CloudNativePG, ver ADR futuro caso/quando adotemos).

## Alternativas consideradas

- **Manter SQLite no dev, Postgres em prod.** Rejeitada. O ponto
  central de um serviço de leilão é concorrência sob contenção; não
  podemos descobrir bugs específicos do Postgres só após o deploy. O
  argumento "ciclo de dev barato" é dominado por Testcontainers + um
  setup Docker estável.
- **Trocar para um banco documental (Mongo, DynamoDB).** Rejeitada. O
  modelo de lance é naturalmente relacional (auction → bids 1:N,
  forte consistência entre `Auction.CurrentPrice` e o último lance
  aceito). O aggregate cabe num store linha-orientado com um único
  token de optimistic concurrency.
- **Cosmos DB / Cloud Spanner.** Rejeitadas por custo e complexidade
  operacional na escala atual do projeto. Revisitar se precisarmos de
  semântica global single-leader.

## Consequências

- Todo dev precisa de Docker. O README cobre o bootstrap.
- Podemos nos apoiar em SQL específico do Postgres onde compensa
  (`FOR UPDATE SKIP LOCKED`, partial indexes), mas essas chamadas
  ficam isoladas dentro das implementações de repositório.
- `EnsureCreatedAsync` saiu; migrations são versionadas e aplicadas
  via o modo de startup `--migrate-only`.
