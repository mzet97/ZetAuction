# Sequência — login

```mermaid
sequenceDiagram
    autonumber
    actor U as Usuário
    participant API as ZetAuction API
    participant RL as RateLimiter (5/5min por IP)
    participant H as LoginCommandHandler
    participant DB as Postgres
    participant PH as PasswordHasher
    participant J as JwtService

    U->>API: POST /api/v1/auth/login {email, password}
    API->>RL: check da partição IP
    RL-->>API: liberado
    API->>H: LoginCommand
    H->>DB: SELECT user WHERE Email=?
    DB-->>H: User{PasswordHash="$2a$11$..."}
    H->>PH: VerifyPassword(password, hash)
    PH->>PH: BCrypt.Verify
    PH-->>H: true
    H->>J: GenerateToken(userId, email, role)
    J->>J: assina HS256 com Jwt:SecretKey
    J-->>H: jwt
    H-->>API: BaseResult<AuthResponse>(token, expiresAt, user)
    API-->>U: 200 OK + token
```

## Modos de falha

- **Senha errada.** `VerifyPassword` retorna `false`; handler
  retorna o deliberadamente vago "Invalid email or password" para
  não vazar qual metade está errada. O custo de verificação do
  BCrypt garante que o canal de tempo é dominado por trabalho de
  hash, não por branch-on-found.
- **Rate limit estourado.** `RateLimiter` retorna `429` com
  `Retry-After: 60` e um body RFC 7807 antes do handler ser
  invocado. Custo do BCrypt não é pago.
- **Outage do banco.** `LoginCommandHandler` falha rápido com o
  caminho padrão de outage Postgres `503`. Sem lógica de caso
  especial.
