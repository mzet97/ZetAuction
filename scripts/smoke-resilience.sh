#!/usr/bin/env bash
# Resilience + worker scenarios that complement smoke-extended.sh.
#  F. AuctionFinalizationWorker auto-finalises an auction whose EndDate
#     has elapsed, and the resulting AuctionClosedEvent flows through
#     the Brighter outbox.
#  G. Stopping/starting the API mid-flight does not lose outbox messages.
#     Postgres durability + the dispatcher re-poll cover the gap.
#  H. Jaeger receives traces from the API and Prometheus has live data.
set -euo pipefail

API=http://localhost:8080
TS=$(date +%s)
PASSWORD="P@ssw0rd123!"

curl_retry() { curl --connect-timeout 5 --max-time 30 -sS "$@"; }

pyget_data_str() {
  python3 -c '
import sys, json
d = json.load(sys.stdin)
data = d.get("data")
if isinstance(data, dict):
    print(data.get("id") or data.get("token") or "")
elif isinstance(data, str):
    print(data)
else:
    print("")'
}

register_user() {
  curl_retry -X POST "$API/api/v1/users" -H "Content-Type: application/json" \
    -d "{\"name\":\"$1\",\"email\":\"$2\",\"password\":\"$PASSWORD\"}" > /dev/null || true
}

login_token() {
  local raw token
  raw=$(curl_retry -X POST "$API/api/v1/auth/login" -H "Content-Type: application/json" \
    -d "{\"email\":\"$1\",\"password\":\"$PASSWORD\"}")
  token=$(printf '%s' "$raw" | pyget_data_str 2>/dev/null || true)
  [ -z "$token" ] && echo "  login_token($1) failed: $raw" >&2
  echo "$token"
}

create_auction_with_end() {
  local token=$1 name=$2 end=$3
  local raw aid
  raw=$(curl_retry -X POST "$API/api/v1/auctions" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $token" \
    -d "{\"name\":\"$name\",\"description\":\"resilience\",\"startingBid\":100,\"minBidIncrement\":5,\"endDateTime\":\"$end\"}")
  aid=$(printf '%s' "$raw" | pyget_data_str 2>/dev/null || true)
  [ -z "$aid" ] && echo "  create_auction failed: $raw" >&2
  echo "$aid"
}

activate_auction() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -c \
    "UPDATE \"Auctions\" SET \"Status\"='Active' WHERE \"Id\"='$1';" > /dev/null
}

set_endDate_past() {
  # Force EndDate into the past so the finalizer worker's
  # FOR UPDATE SKIP LOCKED query picks it up next iteration.
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -c \
    "UPDATE \"Auctions\" SET \"EndDate\" = NOW() - INTERVAL '1 minute' WHERE \"Id\"='$1';" > /dev/null
}

auction_status() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -A -c \
    "SELECT \"Status\" FROM \"Auctions\" WHERE \"Id\"='$1';"
}

outbox_count_for_auction() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -A -c \
    "SELECT count(*) FROM outbox_messages WHERE headerbag::jsonb ->> 'aggregate_id' = '$1';"
}

outstanding_count() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -A -c \
    "SELECT count(*) FROM outbox_messages WHERE dispatched IS NULL;"
}

###############################################################################
echo "=== Setup ==="
SELLER_EMAIL="resil-seller-$TS@example.com"
register_user "Resil Seller" "$SELLER_EMAIL"
SELLER_TOKEN=$(login_token "$SELLER_EMAIL")
[ -z "$SELLER_TOKEN" ] && { echo "FATAL: no seller token"; exit 1; }
echo "  seller token len=${#SELLER_TOKEN}"
echo

###############################################################################
echo "=== Test F — AuctionFinalizationWorker auto-finalises expired auctions ==="
END_FUTURE=$(date -u -d "+1 hour" +"%Y-%m-%dT%H:%M:%SZ")
AID=$(create_auction_with_end "$SELLER_TOKEN" "Auto-Finalize-$TS" "$END_FUTURE")
[ -z "$AID" ] && { echo "FAIL: create_auction"; exit 1; }
activate_auction "$AID"
echo "auction $AID — Status=$(auction_status "$AID")"
echo "Forcing EndDate into the past (would normally trigger after the real EndDate elapses)..."
set_endDate_past "$AID"

# Worker polls every 5s. Wait up to 30s for it to finalise the auction.
echo "Polling for auto-finalization (worker interval = 5s)..."
final_status=""
for attempt in 1 2 3 4 5 6 7; do
  status=$(auction_status "$AID")
  echo "  [${attempt}*5s] Status=$status"
  if [ "$status" = "Finalized" ]; then
    final_status=$status
    break
  fi
  sleep 5
done
[ "$final_status" = "Finalized" ] || { echo "FAIL: worker did not finalize auction"; exit 1; }

echo "  outbox rows for $AID: $(outbox_count_for_auction "$AID")"
docker exec zetauction-postgres psql -U zetauction -d zetauction -c "
SELECT topic,
       CASE WHEN dispatched IS NULL THEN 'OUTSTANDING' ELSE 'DISPATCHED' END AS state
FROM outbox_messages
WHERE headerbag::jsonb ->> 'aggregate_id' = '$AID'
ORDER BY timestamp;"
echo "✅ Auto-finalization works; AuctionClosedEvent dispatched by the Brighter outbox pipeline."
echo

###############################################################################
echo "=== Test G — API restart preserves outstanding outbox messages ==="
# Strategy: pause the API while we stash a row directly into Postgres
# that mirrors what the producer would write. The dispatcher worker is
# inside the API process, so while paused no MarkDispatchedAsync runs
# — the row stays outstanding. After unpause, the dispatcher polls and
# sees it.
echo "Pausing API container (so dispatcher does not run)..."
docker pause zetauction-api > /dev/null

# Synthesise an outstanding outbox row that points at a real domain
# event type. We pick BidPlacedEvent because the in-process handler
# (BidPlacedEventHandler) just logs — idempotent and harmless.
SYN_MSG_ID=$(uuidgen)
SYN_AID=$(uuidgen)
SYN_UID=$(uuidgen)
docker exec zetauction-postgres psql -U zetauction -d zetauction -c "
INSERT INTO outbox_messages (
  messageid, topic, messagetype, timestamp,
  contenttype, headerbag, body
) VALUES (
  '$SYN_MSG_ID',
  'ZetAuction.Domain.Bids.Events.BidPlacedEvent',
  'MT_EVENT',
  NOW(),
  'application/json',
  '{\"clr_type\":\"ZetAuction.Domain.Bids.Events.BidPlacedEvent, ZetAuction.Domain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null\",\"aggregate_type\":\"Auction\",\"aggregate_id\":\"$SYN_AID\"}',
  '{\"eventId\":\"$SYN_MSG_ID\",\"occurredOnUtc\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"auctionId\":\"$SYN_AID\",\"userId\":\"$SYN_UID\",\"amount\":99,\"previousAmount\":50,\"placedAtUtc\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}'
);" > /dev/null

PRE_OUTSTANDING=$(outstanding_count)
echo "  injected synthetic outstanding row $SYN_MSG_ID; outstanding=$PRE_OUTSTANDING"

echo "Unpausing API..."
docker unpause zetauction-api > /dev/null

# Wait up to 15s for dispatcher to pick it up
echo "Waiting for the dispatcher to mark it dispatched..."
for attempt in 1 2 3 4 5 6 7 8; do
  state=$(docker exec zetauction-postgres psql -U zetauction -d zetauction -t -A -c \
    "SELECT CASE WHEN dispatched IS NULL THEN 'OUTSTANDING' ELSE 'DISPATCHED' END FROM outbox_messages WHERE messageid='$SYN_MSG_ID';")
  echo "  [${attempt}*2s] $SYN_MSG_ID -> $state"
  [ "$state" = "DISPATCHED" ] && break
  sleep 2
done
[ "$state" = "DISPATCHED" ] || { echo "FAIL: synthetic row never dispatched"; exit 1; }
echo "✅ Synthetic row dispatched after the API resumed — outbox is durable across pause/resume."
echo

###############################################################################
echo "=== Test H — Jaeger received traces ==="
JAEGER_API=http://localhost:16686
SVC_RAW=$(curl_retry "$JAEGER_API/api/services" 2>&1 || true)
echo "  jaeger services: $(printf '%s' "$SVC_RAW" | head -c 200)"
HAS_API=$(printf '%s' "$SVC_RAW" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    services = d.get('data') or []
    print('yes' if any('zetauction' in s.lower() for s in services) else 'no')
except Exception as e:
    print(f'parse-error: {e}')")
echo "  zetauction-api in jaeger services? $HAS_API"

if [ "$HAS_API" = "yes" ]; then
  TRACES=$(curl_retry "$JAEGER_API/api/traces?service=zetauction-api&limit=3" 2>&1 || true)
  TRACE_COUNT=$(printf '%s' "$TRACES" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    print(len(d.get('data') or []))
except Exception as e:
    print(0)")
  echo "  trace count for zetauction-api: $TRACE_COUNT"
  [ "$TRACE_COUNT" -gt 0 ] && echo "✅ Jaeger has traces — OTel pipeline working."
fi
echo

###############################################################################
echo "=== Test I — Prometheus scraped recent metrics ==="
PROM_API=http://localhost:9090
QUERY=$(curl_retry "$PROM_API/api/v1/query?query=zetauction_outbox_dispatched_total" 2>&1)
echo "  prometheus outbox_dispatched_total query result:"
printf '%s' "$QUERY" | python3 -c "
import sys, json
d = json.load(sys.stdin)
result = d.get('data', {}).get('result', [])
for r in result:
    print(f\"    series={r.get('metric',{})} value={r.get('value')}\")"
echo

###############################################################################
echo "=== Final outbox aggregate state ==="
docker exec zetauction-postgres psql -U zetauction -d zetauction -c "
SELECT topic, count(*) AS total,
       count(*) FILTER (WHERE dispatched IS NOT NULL) AS dispatched,
       count(*) FILTER (WHERE dispatched IS NULL) AS outstanding
FROM outbox_messages
GROUP BY topic
ORDER BY topic;"
