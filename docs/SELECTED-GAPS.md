# Selected Gaps — Follow-up Items

Consolidated backlog of known gaps in the KĪNŪTU platform, split into **Architecture & reliability**
and **Product & commercial**. Each item was **verified against the codebase on 2026-11-01** (as of
`v1.0.0` / commit `f6770cb`) — all twelve are still open; none are secretly implemented.

Effort key: **S** ≈ hours/day, **M** ≈ days, **L** ≈ week(s).

---

## Architecture & reliability

### A1. No full event-replay profile rebuild — *High · M*
Reconciliation (`ReconciliationService.ReconcileMemberAsync`) fixes wallet balances and `PointsEarned`
from ledger postings, but a true value-profile rebuild from the full event history (to recover a
corrupted or mis-evaluated profile) does not exist. This is the platform's main disaster-recovery gap
for profile state.

### A2. Worker/API migration ordering is a manual rule, not enforced — *High · S–M*
The API runs EF migrations at startup (`DbBootstrap.InitializeAsync`); the Worker only calls
`AddResourceSetupOnStartup` and assumes the schema already exists, so it must be started after the API.
Nothing prevents starting them in the wrong order or two API replicas racing migrations. Options: a
dedicated migration step/job, or an advisory-lock guard around migrate-on-startup.

### A3. Kafka transport remains deferred — *Medium · doc, then L if needed*
Only RabbitMQ and the in-DB Postgres queue exist (RabbitMQ shipped in P18). Acceptable at current
scale; worth documenting the ceiling (expected max throughput / tenant count) so "when do we need
Kafka" isn't open-ended.

### A4. Budget attribution under campaign stacking is an approximation — *Medium · M*
When multiple campaigns stack on one event, `CampaignEngine` decrements each campaign's
`GlobalBudgetRemaining` independently; it is not an exact accounting of which campaign's budget paid
for which issued points. Documented as "acceptable approximation; revisit if needed" — can under/over-
charge a budget in edge cases.

### A5. Webhook delivery is a hand-rolled polling dispatcher — *Medium · M*
`WebhookDispatcherJob` is a `BackgroundService` that polls (`Task.Delay` → `DispatchPendingAsync`)
rather than routing through the same Wolverine/outbox transport as everything else (a Phase 4 shortcut
never revisited). Two different "deliver this reliably" mechanisms is extra surface area to maintain.

### A6. No consent / marketing-policy gate on webhooks or notifications — *Medium · M*
Out of scope so far because webhooks are system-to-system, but if any customer-facing
marketing/notification feature is built on the result-event feed, nothing currently enforces consent.
Becomes a prerequisite the moment A7 (below) ships.

---

## Product & commercial

### B1. No outbound notification channels (email / SMS / push) — *High · L*
There's an in-dashboard notification feed and HMAC webhooks, but no actual email/SMS/push delivery
(e.g. "you earned points", birthday reward, OTP for the member app). Everything downstream of the
webhook is the integrator's problem today. Pairs with A6 (consent gate).

### B2. No native POS / e-commerce connectors — 🟡 **Increment 1 done (Shopify)**
Shipped an in-platform **Shopify inbound connector**: per-tenant `Connector` config with an HMAC secret;
`POST /api/v1/connectors` (create, secret returned once) + `GET /api/v1/connectors`; and the webhook
receiver `POST /api/v1/connectors/shopify/{id}/webhook` that **authenticates by HMAC** (per-tenant),
**isolates the tenant** (connector id → application scope), is **idempotent** (Shopify delivery id →
Idempotency-Key), **validates** the payload (422 on bad data, 401 on bad signature), and ingests through
the normal pipeline so events are **logged + replayable** (`/requests`, `/requests/{id}/replay`). Maps
`orders/paid → PurchaseCompleted` and `refunds/create → OrderRefunded` (refund customer resolved from the
original order). Covered by `ShopifyConnectorE2ETests`.
_Remaining:_ WooCommerce/Square connectors, more topics (customer create, order edits), a dashboard UI to
manage connectors, and encrypt-at-rest for the stored secret.

### B3. No fraud / abuse detection on earning — ✅ **Done**
~~Rate limiting is per API key, not per member~~. Implemented a per-tenant, opt-in **per-member earn-rate
cap**: `ApplicationLoyaltySettings.MaxEarnEventsPerHour` (0 = unlimited, the default). The ingestion
service counts a member's accepted earning events in the rolling hour and rejects the overflow with
`429 earn_rate_limited` (duplicates/retries are not counted; only earning event types are limited).
Configure via `PUT /applications/{id}/loyalty-settings` → `maxEarnEventsPerHour`. Covered by
`MemberEarnRateLimitE2ETests`. A broader velocity/scoring model remains a possible future enhancement.

### B4. No cash-out / payout path for redemptions — *Medium · L*
Rewards are catalog items/coupons; there's no gift-card or bank-payout integration for cashing out
points.

### B5. No unified admin audit trail — ✅ **Done**
~~No unified "who changed what, when" log~~. Added an append-only, application-scoped `AuditEntry`
(`audit.entries`) written by a global MVC action filter (`AuditActionFilter`) for every **successful
mutating action by an authenticated admin** (method + path + target id + status + user/role/time; never
request bodies). Readable at `GET /api/v1/admin/audit` under the `ManageUsers` policy (Admin/SuperAdmin,
tenant-scoped). Auditing failures are swallowed + logged so they never break the operation. Covered by
`AuditTrailE2ETests`.

### B6. No per-tenant white-labeling — ✅ **Done**
~~The customer card app is not tenant-brandable~~. Added per-tenant `ApplicationBranding` (name, logo
URL, primary/accent colors; hex-validated): admin `PUT/GET /api/v1/applications/{id}/branding`, and
customer `GET /api/v1/customer/branding` (member-token scoped, no cross-tenant leakage). The customer
card app fetches it and applies the brand's logo/name + tints the card (primary) and points (accent).
Covered by `BrandingE2ETests`. _Remaining:_ full theming across the whole customer shell + a dashboard
settings form; ancestor inheritance of branding.

---

## Suggested sequencing

1. **Quick wins first:** B3 (per-member rate limiting) and B5 (unified audit log) are the smallest and
   highest signal-to-effort.
2. **Operational safety:** A2 (migration ordering guard), then A1 (profile rebuild) for DR.
3. **Reliability cleanup:** A5 (webhooks onto the outbox) — removes a parallel reliability mechanism.
4. **Commercial expansion:** B1 + A6 (notifications + consent, together), then B6 (white-label),
   B2 (connectors), B4 (payout).
5. **Scale, on evidence:** A3 (Kafka) and A4 (exact budget attribution) only when metrics justify.

> Note: the original `SELECTED-GAPS.md` cited `docs/AUDIT.md` as its source; that audit doc is not
> currently in the repo. This file supersedes the standalone list.
