// k6 bid-storm scenario.
//
// Drives concurrent bidders against a single auction to exercise the
// optimistic-concurrency retry, the rate limiter, and the outbox
// dispatcher under load. The scenario ramps up to a sustained 200 RPS
// for a minute and then ramps down.
//
// Run:   k6 run -e TOKEN=$JWT tests/load/bid-storm.js
// Tunable env vars:
//   BASE_URL          - target API
//   AUCTION_ID        - active auction
//   TOKEN             - Bearer token (a JWT issued by /api/v1/auth/login)
//   START_BID         - starting bid amount (default 200)
//
// Acceptance criteria:
//   - http_req_failed < 0.5%
//   - p99 latency < 1500ms (we expect the retry to add some tail)
//   - 4xx ratio < 30% (the rate limiter and rules will reject some)

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:8080';
const auctionId = __ENV.AUCTION_ID || '22222222-2222-2222-2222-222222222222';
const token = __ENV.TOKEN || '';
const startBid = parseFloat(__ENV.START_BID || '200');

const accepted = new Counter('zetauction_bids_accepted');
const concurrencyConflicts = new Counter('zetauction_concurrency_conflicts');
const rateLimited = new Counter('zetauction_rate_limited');
const accept_rate = new Rate('zetauction_accept_rate');

export const options = {
  scenarios: {
    storm: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 400,
      stages: [
        { target: 50,  duration: '15s' },
        { target: 200, duration: '30s' },
        { target: 200, duration: '60s' },
        { target: 0,   duration: '15s' },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.005'],
    http_req_duration: ['p(99)<1500'],
    zetauction_accept_rate: ['rate>0.5'],
  },
};

export default function () {
  // Each iteration picks a fresh nonce so duplicate idempotency keys
  // do not collide once that endpoint adopts Idempotency-Key.
  const amount = startBid + Math.random() * 1000;
  const body = JSON.stringify({
    auctionId,
    userId: '00000000-0000-0000-0000-000000000000',
    amount,
  });

  const res = http.post(
    `${baseUrl}/api/v1/auctions/${auctionId}/bids`,
    body,
    {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': token ? `Bearer ${token}` : undefined,
      },
      tags: { endpoint: 'place-bid' },
    },
  );

  if (res.status === 200 || res.status === 201) {
    accepted.add(1);
    accept_rate.add(true);
  } else {
    accept_rate.add(false);
    if (res.status === 409) concurrencyConflicts.add(1);
    if (res.status === 429) rateLimited.add(1);
  }

  check(res, {
    'is 2xx, 4xx-business, or 429': (r) =>
      (r.status >= 200 && r.status < 300) ||
      r.status === 409 ||
      r.status === 422 ||
      r.status === 429,
    'has correlation id': (r) => !!r.headers['X-Correlation-Id'],
  });

  sleep(0.05);
}
