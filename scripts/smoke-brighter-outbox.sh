#!/usr/bin/env bash
# End-to-end smoke test for the Brighter PostgreSqlOutbox refactor.
# Exercises register → login → create auction → activate → place bid,
# then inspects outbox_messages to confirm a BidPlacedEvent landed and
# was dispatched by BrighterOutboxDispatcherWorker.
#
# Run from inside WSL with the docker compose stack already up:
#   bash scripts/smoke-brighter-outbox.sh
set -euo pipefail

API=http://localhost:8080
TS=$(date +%s)
SELLER_EMAIL="seller-${TS}@example.com"
BIDDER_EMAIL="bidder-${TS}@example.com"
PASSWORD="P@ssw0rd123!"

echo "=== 1. Register seller ==="
curl -sS -X POST "$API/api/v1/users" -H "Content-Type: application/json" \
  -d "{\"name\":\"Seller\",\"email\":\"$SELLER_EMAIL\",\"password\":\"$PASSWORD\"}" \
  | head -c 400
echo

echo "=== 2. Register bidder ==="
curl -sS -X POST "$API/api/v1/users" -H "Content-Type: application/json" \
  -d "{\"name\":\"Bidder\",\"email\":\"$BIDDER_EMAIL\",\"password\":\"$PASSWORD\"}" \
  | head -c 400
echo

echo "=== 3. Login as seller (auctions need an authenticated creator) ==="
SELLER_LOGIN=$(curl -sS -X POST "$API/api/v1/auth/login" -H "Content-Type: application/json" \
  -d "{\"email\":\"$SELLER_EMAIL\",\"password\":\"$PASSWORD\"}")
echo "$SELLER_LOGIN" | head -c 400
echo
SELLER_TOKEN=$(printf '%s' "$SELLER_LOGIN" | python3 -c '
import sys, json
d = json.load(sys.stdin)
print(d.get("data", {}).get("token") or d.get("token") or "")')
echo "seller token len=${#SELLER_TOKEN}"
[ -z "$SELLER_TOKEN" ] && { echo "no seller token"; exit 1; }

echo "=== 4. Login as bidder ==="
BIDDER_LOGIN=$(curl -sS -X POST "$API/api/v1/auth/login" -H "Content-Type: application/json" \
  -d "{\"email\":\"$BIDDER_EMAIL\",\"password\":\"$PASSWORD\"}")
BIDDER_TOKEN=$(printf '%s' "$BIDDER_LOGIN" | python3 -c '
import sys, json
d = json.load(sys.stdin)
print(d.get("data", {}).get("token") or d.get("token") or "")')
echo "bidder token len=${#BIDDER_TOKEN}"
[ -z "$BIDDER_TOKEN" ] && { echo "no bidder token"; exit 1; }

echo "=== 5. Create auction ==="
END=$(date -u -d "+1 hour" +"%Y-%m-%dT%H:%M:%SZ")
AUCTION=$(curl -sS -X POST "$API/api/v1/auctions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -d "{\"name\":\"BrighterOutboxTest\",\"description\":\"smoke\",\"startingBid\":100,\"minBidIncrement\":5,\"endDateTime\":\"$END\"}")
echo "$AUCTION" | head -c 600
echo
AID=$(printf '%s' "$AUCTION" | python3 -c '
import sys, json
d = json.load(sys.stdin)
data = d.get("data")
if isinstance(data, dict):
    print(data.get("id") or "")
elif isinstance(data, str):
    print(data)
else:
    print(d.get("id") or "")')
echo "auctionId=$AID"
[ -z "$AID" ] && { echo "no auction id"; exit 1; }

echo "=== 6. Activate auction (direct DB update — no HTTP endpoint exists) ==="
docker exec zetauction-postgres psql -U zetauction -d zetauction -c \
  "UPDATE \"Auctions\" SET \"Status\"='Active' WHERE \"Id\"='$AID';"

echo "=== 7. Place bid ==="
curl -sS -X POST "$API/api/v1/auctions/$AID/bids" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $BIDDER_TOKEN" \
  -d '{"amount":150}' | head -c 400
echo

echo "=== 8. Outbox state (sleep 2s for dispatcher) ==="
sleep 2
docker exec zetauction-postgres psql -U zetauction -d zetauction -c "
SELECT messageid,
       topic,
       messagetype,
       CASE WHEN dispatched IS NULL THEN 'OUTSTANDING'
            ELSE 'DISPATCHED@'||dispatched::text END AS state,
       headerbag::jsonb ->> 'clr_type' AS clr_type
FROM outbox_messages
ORDER BY timestamp;"

echo "=== 9. Outstanding count ==="
docker exec zetauction-postgres psql -U zetauction -d zetauction -c "
SELECT count(*) FILTER (WHERE dispatched IS NULL) AS outstanding,
       count(*) FILTER (WHERE dispatched IS NOT NULL) AS dispatched,
       count(*) AS total
FROM outbox_messages;"
