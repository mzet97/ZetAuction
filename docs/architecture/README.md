# Documentação arquitetural

Diagramas C4 em camadas + fluxos de sequência para as partes móveis.
Cada documento se lê de forma independente; juntos formam a visão
geral do sistema.

## Modelo C4

| Nível | Documento |
| --- | --- |
| **C1** Contexto | [c4-context.md](c4-context.md) — sistema, atores, dependências externas |
| **C2** Container | [c4-container.md](c4-container.md) — unidades implantáveis e seus protocolos |
| **C3** Componente | [c4-component.md](c4-component.md) — internals do container da API |

C4 nível 4 (código) intencionalmente não é mantido como documento
separado — o código em si, mais os sequence diagrams abaixo, já
fornecem esse detalhe num grão que continua em sincronia com a
realidade.

## Fluxos de sequência

| Fluxo | Documento |
| --- | --- |
| Colocar lance (happy + contended) | [sequence-bid-placement.md](sequence-bid-placement.md) |
| Finalização do leilão | [sequence-auction-finalization.md](sequence-auction-finalization.md) |
| Dispatch do outbox | [sequence-outbox-dispatch.md](sequence-outbox-dispatch.md) |
| Login + upgrade de senha | [sequence-login.md](sequence-login.md) |

## Cross-references

- [ADRs](../adr/README.md) para o **porquê** por trás de cada decisão
  na arquitetura.
- [Observabilidade](../observability.md) para o contrato de métricas
  e logging que surfaces esses fluxos em produção.
- [Segurança](../security.md) para o lado threat-model da figura.
- [Runbooks](../runbooks/) para playbooks por classe de falha.
