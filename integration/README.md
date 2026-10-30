# Integration Kit — connect ANY system to the Loyalty platform

The Loyalty platform is **generic and multi-tenant**: every customer is an `Application`, and the
same API serves all of them. Hareer is one worked example (`integration/hareer/`), not a special
case. This kit is provider-agnostic.

> منصة الولاء عامة ومتعددة المستأجرين؛ كل عميل هو "تطبيق". حرير مجرد مثال، وليس حالة خاصة.

---

## 1. The integration in three steps

1. **Onboard the customer** → get an API key (`integration/onboard.mjs`).
2. **Emit events** from the customer's system post-commit (`integration/emitter-template.ts`).
3. **(Optional) Backfill** history from an export file (`integration/backfill.mjs`).

## 2. The event contract (the only thing a source system must produce)

```
POST {LOYALTY_API}/api/v1/events
X-Api-Key: <the customer's key>
Idempotency-Key: <stable per-business-event key>
Content-Type: application/json
```
```jsonc
{
  "eventId": "<deterministic uuid per business event>",
  "eventType": "PurchaseCompleted",     // or OrderRefunded / CustomerRegistered / ApplicationLogin / ReferralCompleted / ManualAdjustmentRequested
  "eventVersion": 1,
  "sourceTransactionId": "<order/refund/… id>",
  "customerId": "<the source system's stable customer id>",
  "occurredAt": "<ISO-8601>",
  "correlationId": "<trace id>",
  "attributes": { "orderId": "…", "amount": 50000, "currency": "IQD" }
}
```
- `ApplicationId`, `AccountId`, `SourceSystem` are **derived from the API key** — never sent in the body.
- Returns `202` immediately; processing is async. Poll `GET /api/v1/requests/{requestId}`.
- **Idempotency:** always send a stable `Idempotency-Key` and a deterministic `eventId`; retries never double-award.

Per-event attributes & business keys: see `integration/hareer/README.md` §3 (the mapping is the
same for any provider — only the field names on the source side change).

## 3. Onboard a customer

```bash
# one-time platform bootstrap (root application + root management key)
BOOTSTRAP_TOKEN=dev-bootstrap-token-change-me \
  node integration/onboard.mjs --bootstrap --name "Platform Root"

# onboard any customer as a child application; prints their API key
ROOT_KEY=lk_live_… node integration/onboard.mjs \
  --tenant "Acme Retail" --unit POINT --currency IQD --spend-per-point 1000 --welcome-points 100
```

## 4. Emit events

Copy `integration/emitter-template.ts` into the source system and call the `emit*` functions
post-commit. It is env-gated (`LOYALTY_ENABLED=true`, `LOYALTY_API`, `LOYALTY_API_KEY`),
fire-and-forget, and never throws into the caller's path.

## 5. Backfill history (from an export file — never a source DB)

The source system exports its paid orders to a JSON array; this tool replays them idempotently:

```bash
LOYALTY_API=http://localhost:8080 LOYALTY_API_KEY=lk_live_… \
  node integration/backfill.mjs --file orders.json \
    --event PurchaseCompleted \
    --map-customer userId --map-order id --map-amount payableAmount \
    --map-currency currency --map-occurred occurredAt
```
See `integration/sample-orders-export.json` for the shape. Re-running is safe (business key dedup).

## 6. Verify

- Dashboard **Overview / Notifications**, or `GET /api/v1/analytics/overview`.
- `GET /api/v1/members/by-external-id/{customerId}` → wallet + value profile.
- `GET /api/v1/notifications` → `PointsAwardedV1` feed.

## Files

| File | Purpose |
|---|---|
| `onboard.mjs` | Generic onboarding CLI (bootstrap + per-tenant) |
| `backfill.mjs` | Generic historical backfill from an export file |
| `emitter-template.ts` | Provider-agnostic reference emitter to copy into any source system |
| `sample-orders-export.json` | Example backfill input |
| `hareer/` | **Worked example**: Hareer-specific mapping, emitter, mock, samples |
