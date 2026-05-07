// k6 baseline read-path scenario.
//
// Run:   k6 run tests/load/baseline.js
// Tunable env vars:
//   BASE_URL    - target API (default http://localhost:8080)
//   AUCTION_ID  - existing auction id to read
//   VUS         - virtual users (default 50)
//   DURATION    - test duration (default 1m)
//
// Goal: pin the read SLO. The bid endpoint is in bid-storm.js.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:8080';
const auctionId = __ENV.AUCTION_ID || '22222222-2222-2222-2222-222222222222';

const highestBidLatency = new Trend('zetauction_highest_bid_latency_ms');

export const options = {
  scenarios: {
    constant_load: {
      executor: 'constant-vus',
      vus: parseInt(__ENV.VUS || '50', 10),
      duration: __ENV.DURATION || '1m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.005'],
    http_req_duration: ['p(95)<150', 'p(99)<400'],
    zetauction_highest_bid_latency_ms: ['p(95)<150'],
  },
};

export default function () {
  const url = `${baseUrl}/api/v1/auctions/${auctionId}/bids/highest`;
  const res = http.get(url, { tags: { endpoint: 'highest-bid' } });

  highestBidLatency.add(res.timings.duration);

  check(res, {
    'status is 200 or 404': (r) => r.status === 200 || r.status === 404,
    'has correlation id': (r) => !!r.headers['X-Correlation-Id'],
  });

  sleep(0.5 + Math.random() * 0.5);
}
