#!/usr/bin/env bash
# End-to-end smoke test for the running ZetAuction stack.
#
# Drives the full happy path so a single execution exercises:
#   - register / login (BCrypt + HS256)
#   - auction creation (auto-activated on create — see
#     CreateAuctionCommandHandler)
#   - bid placement
#   - GET /bids/highest (cache + ETag)
#   - bid history pagination
#   - SecurityHeadersMiddleware
#   - X-Correlation-Id round-trip
#   - custom OTel metrics on /metrics
#   - per-IP auth rate limiter

set -euo pipefail

BASE=${BASE:-http://localhost:8080}
PASSWORD='Password123!'

fail() { echo "smoke FAILED: $1" >&2; exit 1; }

check_status() {
    local stage="$1" expected="$2" actual="$3"
    if [[ "$actual" != "$expected" ]]; then
        fail "$stage expected HTTP $expected, got $actual"
    fi
    echo "  $stage: HTTP $actual ok"
}

token_for() {
    local email="$1" pass="$2"
    curl -sS -X POST "$BASE/api/v1/auth/login" \
        -H 'Content-Type: application/json' \
        --data-raw "{\"email\":\"$email\",\"password\":\"$pass\"}" \
        | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["token"])'
}

decode_jwt_header() {
    local jwt="$1"
    local hdr="${jwt%%.*}"
    hdr=$(echo "$hdr" | tr '_-' '/+')
    local pad=$(( (4 - ${#hdr} % 4) % 4 ))
    printf -v hdrpad '%s%s' "$hdr" "$(printf '=%.0s' $(seq 1 $pad))"
    echo "$hdrpad" | base64 -d 2>/dev/null
}

# Fresh emails per run so we never collide with previous smoke runs.
SELLER_EMAIL="seller.$(date +%s%N)@example.com"
BIDDER_EMAIL="bidder.$(date +%s%N)@example.com"

reg() {
    local email="$1"
    curl -sS -X POST "$BASE/api/v1/auth/register" \
        -H 'Content-Type: application/json' \
        --data-raw "{\"name\":\"$email\",\"email\":\"$email\",\"password\":\"$PASSWORD\"}" \
        -w '%{http_code}' -o /tmp/zet_reg.body
}

echo "── 1. Register ──"
check_status "register seller" 201 "$(reg "$SELLER_EMAIL")"
check_status "register bidder" 201 "$(reg "$BIDDER_EMAIL")"

echo
echo "── 2. Login + JWT inspection ──"
SELLER_TOKEN=$(token_for "$SELLER_EMAIL" "$PASSWORD")
BIDDER_TOKEN=$(token_for "$BIDDER_EMAIL" "$PASSWORD")
[[ -n "$SELLER_TOKEN" ]] || fail "seller token empty"
[[ -n "$BIDDER_TOKEN" ]] || fail "bidder token empty"

JWT_HEADER=$(decode_jwt_header "$SELLER_TOKEN")
echo "  JWT header: $JWT_HEADER"
echo "$JWT_HEADER" | grep -q '"alg":"HS256"' || fail "JWT not signed with HS256"

echo
echo "── 3. Create auction (auto-activated on create) ──"
AUC_BODY=$(curl -sS -X POST "$BASE/api/v1/auctions" \
    -H "Authorization: Bearer $SELLER_TOKEN" \
    -H 'Content-Type: application/json' \
    --data-raw '{"name":"Smoke","description":"smoke","startingBid":100,"minBidIncrement":1,"endDateTime":"2027-01-01T00:00:00Z"}')
AUCID=$(echo "$AUC_BODY" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"])')
[[ -n "$AUCID" ]] || fail "no auction id in response: $AUC_BODY"
echo "  auction id: $AUCID"

echo
echo "── 4. Place bid ──"
BID_HTTP=$(curl -sS -X POST "$BASE/api/v1/auctions/$AUCID/bids" \
    -H "Authorization: Bearer $BIDDER_TOKEN" \
    -H 'Content-Type: application/json' \
    --data-raw '{"amount":150}' \
    -w '%{http_code}' -o /tmp/zet_bid.body)
check_status "POST bid" 200 "$BID_HTTP"

echo
echo "── 5. Highest bid (with auth) ──"
HIGHEST_HTTP=$(curl -sS "$BASE/api/v1/auctions/$AUCID/bids/highest" \
    -H "Authorization: Bearer $BIDDER_TOKEN" \
    -w '%{http_code}' -o /tmp/zet_high.body \
    -D /tmp/zet_high.hdr)
check_status "GET highest" 200 "$HIGHEST_HTTP"
HIGH_AMOUNT=$(python3 -c 'import sys,json;print(json.load(open("/tmp/zet_high.body"))["data"]["amount"])')
[[ "$HIGH_AMOUNT" == "150" ]] || fail "expected highest=150, got $HIGH_AMOUNT"
echo "  highest amount = $HIGH_AMOUNT"
grep -i '^etag:' /tmp/zet_high.hdr | head -1
grep -i '^cache-control:' /tmp/zet_high.hdr | head -1

echo
echo "── 6. Bid history ──"
HIST_HTTP=$(curl -sS "$BASE/api/v1/auctions/$AUCID/bids?page=1&pageSize=10" \
    -H "Authorization: Bearer $BIDDER_TOKEN" \
    -w '%{http_code}' -o /tmp/zet_hist.body)
check_status "GET history" 200 "$HIST_HTTP"
HIST_COUNT=$(python3 -c 'import sys,json;d=json.load(open("/tmp/zet_hist.body"));print(len(d.get("data",[])))')
[[ "$HIST_COUNT" -ge 1 ]] || fail "expected >=1 bid in history, got $HIST_COUNT"
echo "  history count = $HIST_COUNT"

echo
echo "── 7. List auctions ──"
LIST_HTTP=$(curl -sS "$BASE/api/v1/auctions?page=1&pageSize=10" \
    -H "Authorization: Bearer $BIDDER_TOKEN" \
    -w '%{http_code}' -o /tmp/zet_list.body)
check_status "GET list" 200 "$LIST_HTTP"

echo
echo "── 8. Security headers ──"
HDRS=$(curl -sS -I "$BASE/api/v1/auctions/$AUCID" -H "Authorization: Bearer $BIDDER_TOKEN")
for h in 'Content-Security-Policy' 'X-Frame-Options' 'X-Content-Type-Options' 'Referrer-Policy' 'Permissions-Policy' 'X-Correlation-Id'; do
    echo "$HDRS" | grep -qi "^$h:" || fail "missing header $h"
    echo "  $h: ok"
done

echo
echo "── 9. Custom metrics ──"
sleep 1
METRICS=$(curl -sS "$BASE/metrics")
echo "$METRICS" | grep -qE '^zetauction_bids_placed_total\{.*\} [1-9]' || fail "zetauction_bids_placed_total < 1"
echo "$METRICS" | grep -qE '^zetauction_outbox_dispatched_total\{.*\} [1-9]' || fail "zetauction_outbox_dispatched_total < 1"
echo "$METRICS" | grep -qE '^zetauction_bid_placement_duration_milliseconds_bucket' || fail "missing bid placement duration histogram"
echo "  bids_placed:        $(echo "$METRICS" | grep -E '^zetauction_bids_placed_total\{' | awk '{print $2}')"
echo "  outbox_dispatched:  $(echo "$METRICS" | grep -E '^zetauction_outbox_dispatched_total\{' | awk '{print $2}')"

echo
echo "── 10. Auth rate-limit (5/5min per IP) ──"
RL_HITS=0
for i in 1 2 3 4 5 6 7 8; do
    code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/auth/login" \
        -H 'Content-Type: application/json' \
        --data-raw "{\"email\":\"nobody.$i@example.com\",\"password\":\"$PASSWORD\"}")
    echo "  attempt $i: $code"
    [[ "$code" == "429" ]] && RL_HITS=$((RL_HITS + 1))
done
[[ $RL_HITS -ge 1 ]] || fail "rate limiter never returned 429 across 8 attempts"
echo "  hit 429 $RL_HITS time(s)"

echo
echo "smoke OK"
