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
cd backend && dotnet test          # 72 tests: 24 unit + 48 Testcontainers end-to-end
```

The E2E tests boot the real API + Wolverine + PostgreSQL (Testcontainers, no mocks). Per-feature suites
cover each module (earn/ledger, CVM, campaigns, tiers, rewards, gamification, engagement, customer app,
RFM + language, expiry + stamp conditions, refund reversal + reporting, raffles, grant-reward, ops,
concurrency), and **`FullJourneyE2ETests`** runs the whole platform in one realistic tenant lifecycle as
an integration release gate.

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
- **P6** generic **integration kit** — onboarding + backfill CLIs, a provider-agnostic emitter
  template, and a guide (`integration/`). Works for **any customer**; Hareer is one worked example.

- **P7** membership **Tiers** (Bronze→Platinum): auto-progression from spend/points/visits,
  earn-multiplier benefits, tier-change events, tier analytics + dashboard page

- **P8** **Rewards & Redemptions**: reward catalog, redeem points, **QR coupons**, reserve→confirm→cancel
- **P9** **Gamification**: achievements/badges, daily streaks, time-boxed challenges, leaderboards
- **P10** **Engagement**: **referral program** (member codes + dual-side rewards, idempotent per
  referral), **stamp cards** (digital punch cards that award + reset when full), and **birthday /
  scheduled rewards** (daily sweep, once-per-year)
- **P11** **Customer-facing app**: member **access tokens** (hashed, Bearer-authenticated, scoped to
  one member), a `/api/v1/customer/*` self-service API (own points/tier/streak/badges, rewards +
  redeem, coupons, stamp progress, history), and a mobile-style **digital loyalty card** at `/card`
  (QR, redeem, history) — same Next.js app, admin mints a shareable link/QR from the Members page
- **P12** **RFM personas + Kurdish**: relative R/F/M quintile scoring per tenant → 9 marketing
  **personas** (Champions … Lost), daily recompute + `/analytics/rfm` distribution + dashboard page;
  and **trilingual EN/AR/KU** — Kurdish (Sorani, RTL) across the dashboard, customer app, and API
  error messages (ku→ar script fallback)

- **P13** **Promotions depth**: **points/store-credit expiry engine** (per-tenant `PointsExpiryDays`,
  FIFO — oldest unspent points expire, recent + reserved + redeemed are protected; daily job +
  `/admin/expire-points` + `PointsExpiredV1`), and **stamp-card conditions + validity** (count only
  events matching attribute conditions, e.g. one service/category, within an optional date window —
  reuses the campaign condition model + rule evaluator)

- **P14** **Refund reversal + reporting**: a refund now also **unwinds the stamp** its purchase earned
  (claws back a completed card + decrements open challenge progress; badges stay permanent), and a
  **promotions report** (`/analytics/promotions` + dashboard) aggregates campaign usage, reward
  redemption funnels + rate, stamp-card completions, AOV, and outstanding points/credit liabilities

- **P15** **Raffles + generic actions**: a **prize-draw engine** (qualifying events — purchase ≥ value,
  bookings, shares — earn weighted **entries**; `POST /raffles/{id}/draw` picks a **weighted-random
  winner**, awards the prize, `RaffleWonV1`), and a **generic `CustomAction` event** (`attributes.action`)
  so any user action (app.shared, app.installed, review…) can drive campaigns / stamp cards / raffles
  dynamically — e.g. *share the app → a raffle voucher*; the profile referral QR already covers
  *scan-to-reward* downloads

- **P16** **GrantReward effect**: a campaign can now **grant a specific reward/coupon directly** (not
  just points) on any qualifying event — e.g. *spend ≥ 100k → free dessert coupon*, *share the app →
  voucher*. Issues a free (0-cost) redemption with a coupon code, honoring inventory + per-member
  limit, idempotent; granted coupons confirm/cancel without touching the ledger. Admin can also grant
  directly (`POST /members/{id}/grant-reward`).

The core platform (P1–P6) is complete and generic/multi-tenant. P7–P16 are a **chaay.io-style** SMB
product expansion (loyalty features; messaging hub out of scope): **Tiers ✅**, **Rewards &
Redemptions ✅**, **Gamification ✅**, **Referrals / Stamp cards / Birthday ✅**, **Customer-facing
app ✅**, **RFM personas + Kurdish ✅**, **Expiry + stamp conditions ✅**, **Refund reversal +
reporting ✅**, **Raffles + custom actions ✅**, **GrantReward effect ✅**. **72 tests green** — incl. a
whole-system **full-journey E2E** (`FullJourneyE2ETests`) that configures every module, runs a realistic
multi-member lifecycle, and asserts the integrated state + that all 12 major result events fire.
