#!/usr/bin/env bash
# Extended end-to-end smoke for the Brighter PostgreSqlOutbox refactor.
# Exercises multiple bids, bid rejection, auction cancel, auction close,
# concurrent bids, and the observability surface (/metrics, /health).
#
# Run from inside WSL with the docker compose stack already up:
#   bash scripts/smoke-extended.sh
#
# Design notes:
#  - All users are registered + logged in ONCE at the top so we stay
#    under the auth rate limit (5 attempts / 300s per IP). Subsequent
#    tests reuse those tokens.
#  - curl_retry uses a single attempt per call. Concatenated retry
#    bodies broke JSON parsers downstream and the API itself is now
#    stable (BackgroundServiceExceptionBehavior=Ignore + no
#    HealthChecksUI).
set -euo pipefail

API=http://localhost:8080
TS=$(date +%s)
PASSWORD="P@ssw0rd123!"

curl_retry() {
  curl --connect-timeout 5 --max-time 30 -sS "$@"
}

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
  local name=$1 email=$2
  curl_retry -X POST "$API/api/v1/users" -H "Content-Type: application/json" \
    -d "{\"name\":\"$name\",\"email\":\"$email\",\"password\":\"$PASSWORD\"}" \
    > /dev/null || true
}

login_token() {
  local email=$1 raw token
  raw=$(curl_retry -X POST "$API/api/v1/auth/login" -H "Content-Type: application/json" \
    -d "{\"email\":\"$email\",\"password\":\"$PASSWORD\"}")
  token=$(printf '%s' "$raw" | pyget_data_str 2>/dev/null || true)
  if [ -z "$token" ]; then
    echo "  login_token($email) FAILED. Raw response was: $raw" >&2
  fi
  echo "$token"
}

create_auction() {
  local token=$1 name=$2
  local end raw aid
  end=$(date -u -d "+1 hour" +"%Y-%m-%dT%H:%M:%SZ")
  raw=$(curl_retry -X POST "$API/api/v1/auctions" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $token" \
    -d "{\"name\":\"$name\",\"description\":\"smoke\",\"startingBid\":100,\"minBidIncrement\":5,\"endDateTime\":\"$end\"}")
  aid=$(printf '%s' "$raw" | pyget_data_str 2>/dev/null || true)
  if [ -z "$aid" ]; then
    echo "  create_auction FAILED. Raw response was: $raw" >&2
  fi
  echo "$aid"
}

activate_auction() {
  local id=$1
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -c \
    "UPDATE \"Auctions\" SET \"Status\"='Active' WHERE \"Id\"='$id';" > /dev/null
}

place_bid() {
  local token=$1 aid=$2 amount=$3
  curl_retry -X POST "$API/api/v1/auctions/$aid/bids" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $token" \
    -d "{\"amount\":$amount}"
}

outbox_count() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -c \
    "SELECT count(*) FROM outbox_messages;" | tr -d ' '
}

outbox_dispatched() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -c \
    "SELECT count(*) FROM outbox_messages WHERE dispatched IS NOT NULL;" | tr -d ' '
}

outbox_outstanding() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -t -c \
    "SELECT count(*) FROM outbox_messages WHERE dispatched IS NULL;" | tr -d ' '
}

outbox_topics() {
  docker exec zetauction-postgres psql -U zetauction -d zetauction -c \
    "SELECT topic, count(*) AS n,
            count(*) FILTER (WHERE dispatched IS NOT NULL) AS dispatched
     FROM outbox_messages
     GROUP BY topic
     ORDER BY topic;"
}

###############################################################################
# Pre-flight: register + login all 5 user pairs ONCE, before the 5/300s
# auth rate limit window can run out.
echo "=== Setup — registering users and collecting tokens ==="
declare -A SELLER_TOKENS BIDDER_TOKENS
for letter in A B C D E; do
  SELLER_EMAIL="seller-$letter-$TS@example.com"
  BIDDER_EMAIL="bidder-$letter-$TS@example.com"
  register_user "Seller $letter" "$SELLER_EMAIL"
  register_user "Bidder $letter" "$BIDDER_EMAIL"
done
# Login budget: auth middleware rate limits to 5 per 300s per IP, AND
# the bid handler rate limits to 5 bids per 300s PER USER. We need
# distinct bidder users for tests that fire >1 bid each, but the total
# logins must stay ≤ 5. Layout:
#   1× seller (used by all tests)
#   1× bidder for Test A (5 bids)
#   1× bidder for Tests B + D (1 reject + 1 winning = 2 bids)
#   1× bidder for Test E (5 parallel bids)
# Total: 4 logins, well under the auth bucket.
SELLER_EMAIL="seller-A-$TS@example.com"
BIDDER_A_EMAIL="bidder-A-$TS@example.com"
BIDDER_BD_EMAIL="bidder-B-$TS@example.com"
BIDDER_E_EMAIL="bidder-E-$TS@example.com"
SELLER_TOKEN=$(login_token "$SELLER_EMAIL")
BIDDER_A_TOKEN=$(login_token "$BIDDER_A_EMAIL")
BIDDER_BD_TOKEN=$(login_token "$BIDDER_BD_EMAIL")
BIDDER_E_TOKEN=$(login_token "$BIDDER_E_EMAIL")
[ -z "$SELLER_TOKEN" ]    && { echo "FATAL: seller token empty"; exit 1; }
[ -z "$BIDDER_A_TOKEN" ]  && { echo "FATAL: bidder A token empty"; exit 1; }
[ -z "$BIDDER_BD_TOKEN" ] && { echo "FATAL: bidder BD token empty"; exit 1; }
[ -z "$BIDDER_E_TOKEN" ]  && { echo "FATAL: bidder E token empty"; exit 1; }
echo "  seller    = $SELLER_EMAIL (token len=${#SELLER_TOKEN})"
echo "  bidder A  = $BIDDER_A_EMAIL (token len=${#BIDDER_A_TOKEN})"
echo "  bidder BD = $BIDDER_BD_EMAIL (token len=${#BIDDER_BD_TOKEN})"
echo "  bidder E  = $BIDDER_E_EMAIL (token len=${#BIDDER_E_TOKEN})"

BASELINE_TOTAL=$(outbox_count)
echo "=== Baseline outbox total: $BASELINE_TOTAL ==="
echo

###############################################################################
echo "=== Test A — multiple bids on same auction ==="
AID=$(create_auction "$SELLER_TOKEN" "MultiBid-$TS")
[ -z "$AID" ] && { echo "FAIL: A auction id empty"; exit 1; }
activate_auction "$AID"
echo "auction: $AID"
for amount in 110 120 135 150 165; do
  resp=$(place_bid "$BIDDER_A_TOKEN" "$AID" "$amount")
  echo "  bid $amount → $resp"
done
sleep 2
echo "outbox total after Test A: $(outbox_count) (expect baseline + 5 BidPlacedEvent rows)"
echo

###############################################################################
echo "=== Test B — bid rejection (insufficient amount) writes no outbox row ==="
AID=$(create_auction "$SELLER_TOKEN" "Reject-$TS")
[ -z "$AID" ] && { echo "FAIL: B auction id empty"; exit 1; }
activate_auction "$AID"
echo "auction: $AID, current price = 100, increment = 5"
PRE=$(outbox_count)
resp=$(place_bid "$BIDDER_BD_TOKEN" "$AID" 50)
echo "  bid 50 (below starting price) → $resp"
sleep 1
POST=$(outbox_count)
echo "outbox delta: $((POST - PRE)) (expect 0 — bid rejected before SaveChanges)"
echo

###############################################################################
echo "=== Test C — auction cancel emits AuctionCancelledEvent ==="
AID=$(create_auction "$SELLER_TOKEN" "Cancel-$TS")
[ -z "$AID" ] && { echo "FAIL: C auction id empty"; exit 1; }
activate_auction "$AID"
echo "auction: $AID"
PRE=$(outbox_count)
resp=$(curl_retry -X POST "$API/api/v1/auctions/$AID/cancel" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -d '{"reason":"smoke test"}')
echo "  cancel → $resp"
sleep 2
POST=$(outbox_count)
echo "outbox delta: $((POST - PRE)) (expect 1 — AuctionCancelledEvent)"
echo

###############################################################################
echo "=== Test D — auction close emits AuctionClosedEvent ==="
AID=$(create_auction "$SELLER_TOKEN" "Close-$TS")
[ -z "$AID" ] && { echo "FAIL: D auction id empty"; exit 1; }
activate_auction "$AID"
place_bid "$BIDDER_BD_TOKEN" "$AID" 200 > /dev/null  # one bid so the close has a winner candidate
sleep 1
PRE=$(outbox_count)
resp=$(curl_retry -X POST "$API/api/v1/auctions/$AID/close" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $SELLER_TOKEN")
echo "  close → $resp"
sleep 2
POST=$(outbox_count)
echo "outbox delta: $((POST - PRE)) (expect 2 — BidPlacedEvent (200) + AuctionClosedEvent)"
echo

###############################################################################
echo "=== Test E — concurrent bids exercise xmin retry path ==="
AID=$(create_auction "$SELLER_TOKEN" "Concurrent-$TS")
[ -z "$AID" ] && { echo "FAIL: E auction id empty"; exit 1; }
activate_auction "$AID"

PRE=$(outbox_count)
echo "auction: $AID, firing 5 parallel bids with strictly increasing amounts..."
# Each bid must beat the current price by minBidIncrement=5. Some win,
# some fail with InsufficientBid (no outbox row written).
for amount in 110 120 130 140 150; do
  place_bid "$BIDDER_E_TOKEN" "$AID" "$amount" > /dev/null &
done
wait
sleep 3
POST=$(outbox_count)
echo "outbox delta: $((POST - PRE)) (between 1 and 5, depending on race ordering)"
echo

###############################################################################
echo "=== Final outbox state ==="
outbox_topics
echo
echo "totals:"
echo "  outstanding: $(outbox_outstanding)"
echo "  dispatched : $(outbox_dispatched)"
echo "  total      : $(outbox_count)"
echo

###############################################################################
echo "=== Observability ==="
echo "--- /health ---"
curl_retry "$API/health" | head -c 400
echo
echo "--- /metrics (filtered for ZetAuction custom counters) ---"
curl_retry "$API/metrics" 2>&1 | grep -E "^zetauction" | head -15 || echo "(no zetauction_* metrics yet)"
echo
echo "--- Prometheus targets ---"
curl_retry http://localhost:9090/api/v1/targets 2>&1 | python3 -c "
import sys, json
d = json.load(sys.stdin)
for t in d.get('data', {}).get('activeTargets', []):
    job = t.get('labels', {}).get('job', '?')
    health = t.get('health', '?')
    url = t.get('scrapeUrl', '')
    print(f'  {job:20} {health:10} {url}')" 2>&1 | head -15
