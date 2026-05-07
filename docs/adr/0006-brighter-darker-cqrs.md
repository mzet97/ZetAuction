# ADR-0006: Brighter + Darker para dispatch CQRS

- Status: Accepted
- Data: 2026-05-07

## Contexto

A primeira revisão tratou a inclusão de Brighter (command bus) e
Darker (query bus) como over-engineering — o projeto tem só um punhado
de handlers. Depois de trabalhar pelo resto do sistema, a decisão
envelheceu melhor do que a crítica sugeria:

- O outbox transacional (ADR-0003) usa diretamente o
  `Paramore.Brighter.Outbox.PostgreSql` v9.9.13 para storage da
  tabela `outbox_messages`, e o pipeline in-process do Brighter
  para dispatch dos eventos para handlers. Registro, dispatch,
  políticas de retry e hooks de observabilidade já estão no lugar.
- O lado de query quer um lifecycle diferente do de command (sem
  domain events, sem retry, sem transaction scope). A separação no
  Darker deixa isso explícito no nível de tipo em vez de via padrões
  ad-hoc dentro de um `IMediator`.
- Ganhamos uma source OpenTelemetry para dispatch de command + query
  (ver Phase 6) sem escrever instrumentação custom.

O custo é um registro DI e um punhado de classes base
`RequestHandlerAsync`. É um preço baixo comparado à alternativa —
"montar seu próprio command bus" — que acabaríamos reconstruindo assim
que precisássemos de políticas de retry no dispatch.

## Decisão

- **Commands** fluem pelo Brighter (`IAmACommandProcessor`).
- **Queries** fluem pelo Darker (`IQueryProcessor`).
- O dispatcher do outbox publica via Brighter então qualquer
  subscriber registrado no mesmo pipeline pega o evento.
- Registro de handler é via `AutoFromAssemblies`, então adicionar um
  novo handler é um arquivo novo, sem encanamento DI.

## Alternativas consideradas

- **MediatR.** Rejeitada. A decisão de licenciamento do autor mudou
  no meio do projeto; preferimos depender de um par estável de
  duplo-propósito (Brighter + Darker, ambos Apache-licensed) em vez
  de refazer essa decisão depois.
- **Fazer nosso próprio dispatcher.** Rejeitada. Reimplementa
  features de pipeline que o Brighter já entrega. O argumento "só
  temos N handlers" não considera as necessidades do dispatcher do
  outbox.
- **Chamadas diretas de método do endpoint para o handler.**
  Rejeitada. Perde o pipeline OTel, políticas de retry e o seam
  natural onde mensagens do outbox reentram no sistema.

## Consequências

- Duas abstrações para aprender em vez de uma. O custo é pago uma
  vez; o README + este ADR cobrem os seams.
- O `BrighterOutboxDispatcherWorker` usa reflection para invocar
  `PublishAsync<T>` porque o tipo de mensagem (lido do
  `Header.Bag["clr_type"]`) só é conhecido em runtime. É um
  trade-off deliberado — ver o source do dispatcher para a racional
  e a cobertura de teste.
