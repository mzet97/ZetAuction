# infra/

Infrastructure-as-code e platform assets que vão junto com a
aplicação.

Layout:

- `postgres/` — scripts de init e overrides de tuning para Postgres
  16 (Phase 1).
- `grafana/dashboards/` — dashboards do Grafana versionados,
  exportados como JSON (Phase 6).
- `helm/zetauction/` — Helm chart para deployments Kubernetes
  (Phase 9).

Qualquer coisa específica de ambiente (secrets, hostnames,
parâmetros de scaling) fica parametrizada. Os defaults são tunados
para a stack Docker Compose local documentada no `README.md` da
raiz.
