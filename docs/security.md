# Modelo de segurança

ZetAuction roda como uma API exposta publicamente; o modelo de
segurança abaixo é o que dá ao operador uma baseline defensável. Cada
seção nomeia a ameaça, a mitigação e o arquivo onde você verifica.

## Autenticação

| Preocupação | Mitigação | Onde |
| --- | --- | --- |
| Algorithm confusion (`alg=none`, troca de algoritmo) | `TokenValidationParameters.ValidAlgorithms` é hard-coded em HS256; nenhum outro algoritmo valida | `AuthConfig`, `JwtService.ValidateToken` |
| Segredo de assinatura fraco | `JwtService` recusa subir se `Jwt:SecretKey` tiver menos de 32 caracteres | `JwtService` |
| Tokens stale após revogação | `ClockSkew = TimeSpan.Zero`, lifetime default de 1h. Refresh tokens long-lived deliberadamente não foram implementados ainda | `JwtService` |
| Vazamento do segredo HMAC entre serviços | Em produção o segredo é injetado via Kubernetes Secret / Vault; nunca é commitado e a rotação é feita por deploy de um novo secret e roll de todas as réplicas | `chart/zetauction/templates/secret.yaml`, `docker-compose.yml` |

## Armazenamento de senha

| Preocupação | Mitigação | Onde |
| --- | --- | --- |
| Hashes baratos de atacar (PBKDF2, MD5) | BCrypt com work factor default da lib (≥10 rounds). Verificação é constant-time | `PasswordHasher` |
| Reuso de senha entre breaches | Fora de escopo hoje — nenhum check haveibeenpwned client-side ainda. Anotado para a próxima iteração |  |

## Brute force / credential stuffing

| Preocupação | Mitigação | Onde |
| --- | --- | --- |
| Tentativas de login distribuídas | Rate limiter fixed-window por IP nos endpoints `/api/v1/auth/*` (5 requests / 5min). Retorna RFC 7807 `429` com `Retry-After` | `RateLimitingConfig`, `AuthEndpoints` |
| Stampede de lances | Limiter sliding-window em Redis com fallback de circuit-breaker (Phase 4) | `RedisRateLimiterService` |

## Headers de transporte / resposta

`SecurityHeadersMiddleware` define o set canônico de hardening em
toda resposta (incluindo 4xx/5xx):

- `Strict-Transport-Security` (só em HTTPS)
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy` negando toda feature que a API não precisa
- `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`
- `Server` e `X-Powered-By` são removidos

## Operacional

- O segredo HMAC do JWT (`Jwt:SecretKey`) é injetado por env var em
  todo ambiente. Em produção tem que vir de um Kubernetes Secret
  montado via `secretKeyRef` ou de um KMS / Vault.
- O container da API roda com `no-new-privileges:true`.
- Healthchecks (`/health`) e métricas (`/metrics`) são
  não-autenticados por design — manter fora da ingress pública.
