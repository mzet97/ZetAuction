# docs/api-examples/

Exemplos de curl e HTTP request para a API pública. O contrato
canônico é o documento OpenAPI em `/openapi/v1.json`; os snippets
aqui são guidance, não especificação.

Estrutura:

- `curl/` — snippets de shell, prontos para copiar/colar.
- `http/` — arquivos `.http` compatíveis com as extensões
  `httpyac`/REST Client.
- `postman/` — coleção Postman 2.1 exportada do documento OpenAPI
  (re-exporte, não edite à mão).

Artefatos gerados precisam ser regeneráveis a partir da fonte
OpenAPI:

```bash
scripts/regen-api-examples.sh
```
