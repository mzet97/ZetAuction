# Política de segurança

## Versões suportadas

Apenas o último release tagueado em `main` recebe patches de
segurança. Versões mais antigas não são suportadas.

## Reportando uma vulnerabilidade

Mande email para o maintainer em
**matheus.zeitune.developer@gmail.com** com:

- Descrição do problema e o impacto que você observou.
- Passos para reproduzir (uma proof of concept mínima é preferida).
- O hash do commit ou tag onde você observou o problema.
- Quaisquer logs, payloads de request ou respostas que demonstrem o
  problema.

**Não** abra uma issue pública no GitHub, pull request ou discussion
para uma suspeita de vulnerabilidade.

## Targets de resposta

| Passo                               | Target |
|-------------------------------------|--------|
| Reconhecimento                      | 2 dias úteis |
| Triage inicial e severity rating    | 5 dias úteis |
| Fix ou mitigação em `main`          | 30 dias para high/critical |
| Disclosure pública                  | Depois que um release com fix estiver disponível |

Seguimos CVSS v3.1 para scoring de severidade. Vamos creditar o
reporter nas release notes a menos que você prefira explicitamente o
contrário.

## Modelo de ameaça em escopo

O serviço é desenhado contra as assumptions documentadas em
`docs/security/asvs.md`. Categorias-chave que consideramos em
escopo:

- Bypass de autenticação em `/api/auth/*`.
- Falhas de autorização nos endpoints de auction e bid.
- Falhas de concorrência ou replay que permitam colocar lances
  duplicados ou inválidos.
- Bypass do rate limiter.
- Vazamento de informação em respostas de erro.
- Vulnerabilidades em dependências (rodamos
  `dotnet list package --vulnerable` e `trivy` em CI).

## Fora de escopo

- Denial of service causado por exaurir os recursos do host fora dos
  limites documentados.
- Vulnerabilidades em dependências de terceiros que já foram
  divulgadas e têm fix publicado; abra uma issue regular
  referenciando o advisory.
- Findings que exigem acesso físico ao host ou infraestrutura
  comprometida.
