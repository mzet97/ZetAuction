# StableBid — Auction Service — Senior .NET Backend Assessment

**Time estimate:** ~4 hours of focused work
**Deadline:** 5 days from receipt
**Stack:** .NET 9, C#, EF Core, SQLite

---

## Overview

Build a **real-time Auction Service** — a .NET 9 Web API where users can create auctions, place bids, and query auction state. The system must handle concurrent bidding safely, enforce rate limits, and finalize auctions correctly when they reach their end time.

We are evaluating **architecture, good practices and design decisions**, particularly how you handle edge cases. Assume there will be **more than one instance of this service running simultaneously** behind a load balancer — your design must account for this.

---

## What You'll Build

### 1. User Authentication

Keep it simple — no OAuth or IAM required.

- `POST /api/auth/register` — Create a user account (login + password)
- `POST /api/auth/login` — Authenticate and return credentials

Passwords must be hashed. All other endpoints require an authenticated user.

> **Bonus:** Issue JWT tokens for authentication. We won't penalize a simpler approach (e.g., session-based), but JWT is preferred given the multi-instance constraint.

---

### 2. Auction Management

#### `POST /api/auctions`

Authenticated users can create auctions with the following fields:

| Field | Description |
|---|---|
| `name` | Display name for the auction |
| `startingBid` | The minimum amount for the first bid |
| `minBidIncrement` | The minimum amount a new bid must exceed the current highest bid by |
| `endDateTime` | When the auction closes (UTC) |

**Rules:**

- `endDateTime` must be in the future.
- `startingBid` and `minBidIncrement` must be positive values.
- The creator of an auction **cannot** bid on their own auction.

#### `GET /api/auctions`

List auctions. Support filtering by status (`active`, `finalized`) and pagination.

#### `GET /api/auctions/{id}`

Return auction details including its current status and highest bid.

---

### 3. Placing Bids

#### `POST /api/auctions/{id}/bids`

An authenticated user places a bid on an auction they did not create.

**Validation rules:**

- The auction must be **active** (not finalized, not past its `endDateTime`).
- If there are no bids yet, the bid must be **≥ `startingBid`**.
- If there are existing bids, the bid must be **≥ highest bid + `minBidIncrement`**.
- If the bid does not meet the above criteria, it must be **rejected** with a clear error message.

#### Rate Limiting

Each user is limited to **5 bids per 5-minute sliding window**, across all auctions.

- Return `429 Too Many Requests` when the limit is exceeded.
- This must work correctly **across multiple service instances**.

---

### 4. Querying the Current Highest Bid

#### `GET /api/auctions/{id}/bids/highest`

Returns the current highest bid for a given auction.

> **Think about:** This endpoint will be called **frequently** — assume clients poll it regularly.

---

### 5. Auction Finalization

When an auction reaches its `endDateTime`, it must be **finalized**:

- If there are bids: store the **winning bid** (highest bid at close time) and mark the auction as finalized.
- If there are no bids: mark the auction as finalized with a **no-bids** outcome.
- Once finalized, the auction **must not accept any more bids**.

---

### 6. Tests

We expect both **unit tests** and **integration tests**. What you test and how you structure your test suite is part of the evaluation — use your judgement on what's worth covering.

---

## Technical Requirements

- **.NET 9** Web API
- **EF Core** with **SQLite** (you may introduce or change the infrastructure if your design requires it)
- A `README.md` with:
  - How to run the service
  - How to run tests
  - Architecture decisions and trade-offs — particularly around concurrency, caching, and multi-instance concerns
  - Any assumptions you made

---

## What We're Looking For

This is a senior-level assessment. We care more about **how you think** and how you structure your solution, following best practices and clear documentation. Specifically, if you make a trade-off (e.g., eventual consistency on reads for throughput), **document it**.

---

## Bonus (if time permits)

- Dockerized setup (Dockerfile + docker-compose)
- Structured logging
- Bid history endpoint with pagination
- Metrics or health check endpoints

---

## Submission

Invite evandro@stablemint.io and sheriton@stablemint.io to a **GitHub repository**

Your repo should include:

- All source code
- Tests
- README with setup instructions and design decisions

---

## Questions?

If anything in this spec is ambiguous, make a reasonable assumption, document it in your README, and move forward. If you hit a blocker that prevents you from continuing, please email evandro@stablemint.io and we will get back to you as soon as possible.

Good luck.
