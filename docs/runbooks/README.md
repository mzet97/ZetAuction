# docs/runbooks/

Documentação on-call para incidentes em produção. Todo runbook segue
a estrutura:

1. **Sintoma** — o que o operador observa (alerta, dashboard,
   relato de cliente).
2. **Severidade** — SEV1/SEV2/SEV3 com threshold quantitativo.
3. **Diagnóstico** — comandos e dashboards para confirmar o modo de
   falha.
4. **Mitigação** — ação mínima necessária para restaurar o serviço.
5. **Recuperação** — passos completos de restauração, incluindo
   validação de dados.
6. **Postmortem** — link para o template Five Whys depois do
   incidente fechado.

Runbooks são owned pelo mesmo time que owna o code path relacionado
(ver `CODEOWNERS`).
