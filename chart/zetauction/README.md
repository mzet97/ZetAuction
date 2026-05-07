# Helm chart zetauction

Implanta a API ZetAuction junto com seu job de migração de
bootstrap, Service, Ingress (opcional), HPA, PodDisruptionBudget e
ServiceMonitor.

O chart **não** implanta Postgres, Redis ou a stack de
observabilidade — essas são dependências de infra que devem ser
gerenciadas fora (ex.: CloudNativePG, Bitnami Redis,
kube-prometheus-stack) e apontadas via `values.yaml`.

## Install

```bash
helm install zetauction ./chart/zetauction \
  --namespace zetauction --create-namespace \
  --set secrets.database.connectionString="Host=postgres;..." \
  --set secrets.redis.connectionString="redis:6379" \
  --set secrets.jwt.secretKey="$(openssl rand -base64 48)"
```

`secrets.jwt.secretKey` precisa ter pelo menos 32 caracteres — a API
recusa subir com um valor menor.

Em produção, prefira um secret pre-existente para evitar que o Helm
renderize o material da chave dentro do manifest do release:

```bash
kubectl create secret generic zetauction-secrets \
  --namespace zetauction \
  --from-literal=jwt-secret="$(openssl rand -base64 48)" \
  --from-literal=database-connection=... \
  --from-literal=redis-connection=...

helm install zetauction ./chart/zetauction \
  --namespace zetauction \
  --set secrets.existingSecret=zetauction-secrets
```

## Upgrades

O job de migration roda como hook `pre-install` / `pre-upgrade` do
Helm e trava o rollout do deployment até ter sucesso. Migrations do
EF Core adquirem um advisory lock então múltiplas réplicas do job
não vão corromper umas às outras se um deploy for tentado de novo.

## Verify

```bash
helm test zetauction --namespace zetauction
kubectl port-forward svc/zetauction 8080:8080 -n zetauction
curl http://localhost:8080/health
```

## Referência de values

Ver `values.yaml` para o conjunto completo. Os grupos mais comumente
sobrescritos:

| Grupo | Propósito |
| --- | --- |
| `image.repository`, `image.tag` | Fixa a imagem que está sendo implantada |
| `replicaCount`, `autoscaling.*` | História de scaling |
| `secrets.*` | Connection strings + segredo HMAC do JWT |
| `config.otel.otlpEndpoint` | Endpoint do OTel Collector |
| `ingress.*` | Hostname público + TLS |
| `serviceMonitor.enabled` | Integração com operator do Prometheus |
