# Contribuindo com o ZetAuction

Obrigado por considerar contribuir. Esse serviço roda como parte de
uma plataforma de leilão em tempo real; correção sob concorrência e
deployment multi-instância é a régua principal de qualidade.

## Setup de desenvolvimento

Pré-requisitos:

- .NET 9 SDK
- Docker + Docker Compose v2 (Docker Engine dentro do WSL 2 em hosts Windows)
- Um shell POSIX para os scripts auxiliares (use WSL no Windows)

Stack local:

```bash
docker compose up --build
```

Rodar as suítes de teste:

```bash
dotnet test ZetAuction.slnx
```

## Modelo de branching

- `main` é protegida. Pushes diretos não são permitidos.
- Trabalho de feature em branches `feat/<topico-curto>`.
- Defeitos em `fix/<topico-curto>`.
- Manutenção em `chore/<topico-curto>`.
- Mudanças só de doc em `docs/<topico-curto>`.

Abre um pull request contra `main`. CI tem que passar antes do
merge. Squash-merge a menos que a branch já tenha um histórico de
commits coerente que valha preservar.

## Mensagens de commit

Segue [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <subject>

<body>

<footer>
```

Tipos permitidos: `feat`, `fix`, `chore`, `docs`, `refactor`, `perf`,
`test`, `build`, `ci`, `revert`.

Scopes seguem o layout do projeto: `domain`, `application`,
`infrastructure`, `api`, `tests`, `infra`, `ci`, `docs`.

Exemplo:

```
feat(domain): add optimistic concurrency token to Auction aggregate

Mapeia RowVersion para a system column xmin do Postgres via Npgsql.
Conflitos viram DbUpdateConcurrencyException, traduzidos pelo
PlaceBidCommandHandler em ConcurrencyConflictException com retry
Polly. Closes #42.
```

## Estilo de código

- `dotnet format` roda em todo pull request.
- Um change-set por pull request. Refactors e mudança de
  comportamento não compartilham commit.
- APIs públicas exigem comentários XML doc; helpers internos não.
- Testes usam padrão Arrange/Act/Assert com `// Arrange`/`// Act`/`//
  Assert` explícitos só quando a estrutura ficar pouco clara.

## Decisões arquiteturais

Qualquer coisa que seja irreversível barato (escolha de tecnologia,
modelo de deployment, layout de dado) precisa de um Architecture
Decision Record sob `docs/adr/`. Use o template
[MADR](https://adr.github.io/madr/). Referencie o ADR na descrição
do pull request.

## Checklist de pull request

Antes de pedir review, verifica:

- [ ] Testes passam local (`dotnet test`).
- [ ] Sem warnings novos do compiler.
- [ ] Cobertura de código não caiu abaixo do gate (85% line, 75% branch).
- [ ] Se a mudança afeta a API pública, OpenAPI foi regenerado e o spec foi commitado.
- [ ] Se a mudança introduz uma nova dependência externa, um ADR justifica.
- [ ] Se a mudança toca em autenticação, autorização ou persistência, o checklist de segurança em `docs/security/asvs.md` foi revisado.
- [ ] CHANGELOG.md atualizado sob a seção `Unreleased`.

## Reportando issues de segurança

**Não** abra uma issue pública para uma vulnerabilidade. Ver
[SECURITY.md](./SECURITY.md).
