# scripts/

Scripts utilitários operacionais. Todo script tem que ser:

- Idempotente (seguro de rodar de novo).
- POSIX-compatível (`#!/usr/bin/env bash`, sem `bash`-ismos a menos
  que declarado).
- Auto-documentado via um header no topo do arquivo explicando
  propósito, inputs e exit codes.
- Sem secrets — lê configuração de variáveis de ambiente.

Esse diretório é intencionalmente estreito. Qualquer coisa ad-hoc
mora numa branch de dev e nunca chega na `main`.
