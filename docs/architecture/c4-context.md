# C1 — Contexto do sistema

O domínio de leilão no nível mais alto de zoom. Caixas fora da
fronteira "ZetAuction" estão fora do escopo desse codebase mas
importam para o comportamento de runtime.

```mermaid
C4Context
    title ZetAuction — System Context

    Person(bidder, "Bidder", "Usuário final que coloca lances em leilões ativos.")
    Person(seller, "Seller / Auctioneer", "Cria e gerencia leilões.")
    Person(operator, "Operator / SRE", "Faz deploys, observa dashboards, responde a incidentes.")

    System_Boundary(zb, "ZetAuction") {
        System(api, "ZetAuction API", "Aceita lances, finaliza leilões, emite eventos de domínio. .NET 9 + Postgres + Redis.")
    }

    System_Ext(idp, "Identity Provider", "Emite credenciais. Hoje: tabela in-house de usuários; futuro: IdP externo.")
    System_Ext(observability, "Stack de observabilidade", "Grafana + Prometheus + Loki + Jaeger. Recebe traces, métricas, logs.")
    System_Ext(notifier, "Subscribers de notificação", "Consumers downstream de eventos BidPlaced / AuctionClosed. Fora de escopo hoje.")

    Rel(bidder, api, "Coloca lances, lê leilões", "HTTPS")
    Rel(seller, api, "Cria / cancela leilões", "HTTPS")
    Rel(operator, observability, "Observa dashboards, responde a alertas", "HTTPS")

    Rel(api, idp, "Verifica credenciais", "chamada interna")
    Rel(api, observability, "Emite traces, métricas, logs OTLP", "OTLP gRPC")
    Rel(api, notifier, "Publica eventos de domínio", "Pipeline Brighter → outbox dispatcher")
```

## Personas

| Persona | Objetivo |
| --- | --- |
| **Bidder** | Ver o preço atual, colocar um lance, ver o lance dele entrar antes do de outro |
| **Seller** | Criar um leilão, ver o tempo passar, descobrir quem ganhou |
| **Operator** | Detectar outage rápido, entender o que está degradado, seguir runbooks |

## Dependências externas

- **Identity Provider** é interno hoje (aggregate `User` +
  `LoginCommandHandler`). JWT é assinado com HS256; trocar por um IdP
  externo (ou virar para chave assimétrica) é uma mudança contida em
  `JwtService` + `AuthConfig`.
- **Stack de observabilidade** vem com o `docker-compose.yml` para o
  loop local. Produção troca pela stack da plataforma e reusa o
  contrato do endpoint OTLP.
- **Subscribers de notificação** não estão nesse repo. O outbox
  garante delivery at-least-once assim que um subscriber estiver
  conectado (rota Brighter, ver ADR-0003 / ADR-0006).
