#!/usr/bin/env bash
# Inject a synthetic outbox row whose clr_type cannot be resolved by the
# dispatcher worker. Expectation: after PoisonRetryThreshold (10) failed
# attempts, the worker forces Dispatched=now() and increments the
# zetauction_outbox_dead_lettered_total counter.
#
# Pre-flight: docker compose stack must be up.
set -euo pipefail

POISON_ID=$(cat /proc/sys/kernel/random/uuid)
echo "Injecting poison message id=${POISON_ID}"

# topic + clr_type intentionally point at a type that does not exist
# in any loaded assembly. body is a syntactically-valid JSON payload of
# a domain event so we ONLY exercise the type-resolution failure path.
PAYLOAD=$(cat <<JSON
{
  "EventId": "${POISON_ID}",
  "AuctionId": "00000000-0000-0000-0000-000000000000",
  "OccurredOnUtc": "2026-05-07T00:00:00Z"
}
JSON
)

HEADERBAG=$(cat <<JSON
{
  "clr_type": "ZetAuction.NonExistent.PoisonEvent, ZetAuction.NonExistent",
  "aggregate_type": "Auction",
  "aggregate_id": "00000000-0000-0000-0000-000000000000"
}
JSON
)

docker exec -i zetauction-postgres psql -U zetauction -d zetauction <<SQL
INSERT INTO outbox_messages
  (messageid, topic, messagetype, "timestamp", headerbag, body)
VALUES
  ('${POISON_ID}',
   'ZetAuction.NonExistent.PoisonEvent',
   'MT_EVENT',
   NOW(),
   \$\$${HEADERBAG}\$\$,
   \$\$${PAYLOAD}\$\$);

SELECT messageid, topic, dispatched IS NULL AS outstanding
FROM outbox_messages
WHERE messageid = '${POISON_ID}';
SQL

echo ""
echo "Waiting up to 30s for the worker to dead-letter the row (poll interval 500ms × 10 attempts)..."
for i in $(seq 1 30); do
  STATE=$(docker exec -i zetauction-postgres psql -U zetauction -d zetauction -tA <<SQL
SELECT CASE WHEN dispatched IS NULL THEN 'PENDING' ELSE 'DISPATCHED' END
FROM outbox_messages
WHERE messageid = '${POISON_ID}';
SQL
)
  if [ "${STATE}" = "DISPATCHED" ]; then
    echo "Row dead-lettered after ${i}s."
    break
  fi
  sleep 1
done

echo ""
echo "Final row state:"
docker exec -i zetauction-postgres psql -U zetauction -d zetauction <<SQL
SELECT messageid, topic, dispatched
FROM outbox_messages
WHERE messageid = '${POISON_ID}';
SQL

echo ""
echo "Dead-letter metric (should be >= 1):"
curl -fsS http://localhost:8080/metrics | grep -E '^zetauction_outbox_dead_lettered_total' || echo "(metric not yet emitted)"

echo ""
echo "Worker log lines for this poison id:"
docker logs zetauction-api 2>&1 | grep -E "${POISON_ID}|exceeded.*dispatch attempts|dead-letter" | tail -20 || true
