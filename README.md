# Loyalty & Rewards Platform · منصة الولاء والمكافآت

A multi-tenant, event-driven loyalty platform with an immutable double-entry points ledger.
Built to be **dynamic for any customer**; first integration target: **Hareer**.

منصة ولاء متعددة المستأجرين، مبنية على الأحداث، مع دفتر أستاذ مزدوج القيد غير قابل للتعديل.
مصممة لتكون ديناميكية لأي عميل؛ وأول تكامل مستهدف هو **حرير**.

---

## What it does

- **Generic event ingestion** — one API (`POST /api/v1/events`) accepts any loyalty event
  (purchase, refund, registration, login, referral, adjustment), returns `202`, processes async.
- **Multi-tenant by design** — every row belongs to an `Application`; visibility is self + descendants
  via a closure table. API-key credentials per application, hashed + rotatable.
- **Immutable double-entry ledger** — points are balanced debit/credit postings; wallet balances are
  checked projections, never the source of truth.
- **Idempotent at 4 boundaries** — HTTP key, source-event, business-event, and reward-effect/ledger.
- **Bilingual (AR/EN)** — localized API errors via `Accept-Language`, Arabic names on user-facing
  entities, RTL-ready dashboard.

## Architecture

```
backend/   .NET 10 modular monolith — ASP.NET Core + WolverineFx (durable Postgres) + EF Core + PostgreSQL
dashboard/ Next.js (App Router, TS) admin dashboard — bilingual AR/EN, RTL
integration/hareer/  Hareer integration contract + reference emitter + mock demo
docs/      design spec, ADRs, deviations, flow diagram
```

Modules: `Applications · Accounts · Settings · Members · Wallets(ledger) · Campaigns · Rules · Integration`.

## Run it

```bash
# 1. whole stack (Postgres + API)
docker compose up --build        # API on http://localhost:8080  (Scalar UI at /scalar)

# 2. end-to-end Hareer demo (bootstraps + emits a purchase, prints the wallet)
node integration/hareer/mock-emitter.mjs
```

Local dev without Docker (needs .NET 10 + a local Postgres):

```bash
cd backend && dotnet run --project src/Loyalty.Api
```

## Test

```bash
cd backend && dotnet test          # 8 unit + 1 Testcontainers end-to-end release gate
```

The end-to-end test boots the real API + Wolverine + PostgreSQL (Testcontainers, no mocks) and
proves: bootstrap → settings → member → active campaign → purchase event → **points in the wallet**,
plus HTTP idempotency.

## Quick API tour (Phase 1)

```http
POST   /api/v1/bootstrap                         # one-time root app + key (X-Bootstrap-Token)
POST   /api/v1/applications                       # child applications (closure hierarchy)
POST   /api/v1/applications/{id}/accounts         # rotatable API keys
PUT    /api/v1/applications/{id}/loyalty-settings # points unit, currency, spend-per-point
POST   /api/v1/members                            # member + wallet + ledger accounts
PUT    /api/v1/applications/{id}/cvm-settings     # lifecycle thresholds + value thresholds
POST   /api/v1/campaigns  ->  /{id}/activate      # trigger + conditions + effects + audience targeting
POST   /api/v1/segments   ->  /{id}/activate      # rule-based segments over the value profile
POST   /api/v1/events                             # the generic ingestion endpoint (202)
GET    /api/v1/requests/{id}                      # processing status
GET    /api/v1/members/{id}/wallet                # balances + immutable ledger
GET    /api/v1/members/{id}/value-profile         # lifecycle stage, counts, net spend, active segments
GET    /api/v1/analytics/overview                 # members, points issued/reversed, lifecycle mix
GET    /api/v1/notifications                       # result-event feed (PointsAwardedV1, …)
POST   /api/v1/webhooks                            # register an HMAC-signed webhook endpoint
POST   /api/v1/members/{id}/reconcile              # recompute wallet from the ledger; report mismatch
GET    /api/v1/requests?status=…                   # dead-letter / status explorer
POST   /api/v1/requests/{id}/replay                # controlled replay of a stuck request
GET    /health/ready                               # readiness (PostgreSQL)
```

Event types: `PurchaseCompleted`, `OrderRefunded`, `CustomerRegistered`, `ApplicationLogin`,
`ReferralCompleted`, `ManualAdjustmentRequested`.

All business endpoints require `X-Api-Key`. Send `Accept-Language: ar` for Arabic error messages.

## Status

**Phases 1–5 complete and verified — 37 tests** (25 unit + 12 Testcontainers end-to-end):
- **P1** core pipeline (ingestion → ledger, 4-boundary idempotency)
- **P2** CVM (value profiles, lifecycle, rule-based segments, audience targeting, all six event types)
- **P3** full campaign engine (stacking, member limits, global budgets, validate/simulate),
  Microsoft **RulesEngine adapter** (toggle `LOYALTY_RULES_ENGINE`), and **refund reversal**
- **P4** result events + **webhooks** (HMAC-signed delivery), **analytics** overview, frozen CVM
  snapshot + explainable reward decisions, and dashboard depth (Overview / Notifications / Webhooks)
- **P5** reliability/ops: **EF migrations**, durable **PostgreSQL transport + dedicated worker**,
  **reconciliation**, per-key **rate limiting**, `/health/ready`, dead-letter **replay**, **OpenTelemetry**

See `docs/DEVIATIONS.md` for the roadmap (P6 live Hareer integration).
