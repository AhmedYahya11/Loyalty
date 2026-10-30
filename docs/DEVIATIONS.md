# Deviations from the MVP spec (Phases 1–5)

The authoritative design is `docs/design/MVP_IMPLEMENTATION_PLAN.md`. Phase 1 = core pipeline;
Phase 2 = CVM; Phase 3 = full campaign engine + RulesEngine + refund reversal; Phase 4 =
result events + webhooks/notifications + analytics + snapshots; Phase 5 = **reliability/ops**
(EF migrations, Postgres transport + dedicated worker, reconciliation, rate limiting, health,
dead-letter replay, OpenTelemetry). Bilingual (AR/EN) throughout. Documented deltas below.

## Phase 8 status (done) — Rewards & Redemptions

Points are now **spendable** (previously earn-only):
- Reward catalog (name, points cost, type Coupon/Voucher/Discount/FreeItem, value, inventory,
  per-member limit, bilingual) + CRUD endpoints ✅
- Redemption **reserve→confirm→cancel** with a unique coupon code (QR payload) + 24h hold ✅
- Remaining ledger transaction types implemented (§13): **Reserve** (active→reserved), **Release**
  (reserved→active), **Redeem** (reserved→redemption clearing) — balanced postings + wallet buckets ✅
- Balance/inventory/per-member-limit checks; profile **PointsRedeemed** updated on confirm;
  result events RedemptionReservedV1 / PointsRedeemedV1 / RedemptionCancelledV1 ✅
- Dashboard Rewards page + member redeem action with **QR coupon** (qrcode.react) + confirm/cancel ✅
- Tests: 2 Testcontainers E2E (earn→reserve→confirm spends points; cancel releases; insufficient
  points rejected) — 49 green total

Roadmap: P7 Tiers ✅ · **P8 Rewards & Redemptions ✅** → P9 Gamification → P10 Referrals/Stamp
cards/Birthday → P11 Customer-facing app → P12 richer dashboard + RFM + Kurdish.

---

## Phase 7 status (done) — Tiers (product expansion toward chaay-parity)

Target: match a chaay.io-style SMB loyalty product (loyalty features; messaging hub excluded).
P7 adds membership **Tiers**:
- Tier entity (rank, threshold metric NetSpend/PointsEarned/PurchaseCount, threshold, earn
  multiplier, bilingual name, color) + TierHistory audit + CRUD endpoints ✅
- Deterministic tier evaluation (highest tier met); evaluated in the worker after the profile
  update; `MemberTierChangedV1` result event on change ✅
- Tier **earn-multiplier benefit** joins the "highest eligible multiplier" applied to base points ✅
- Tier on the member value-profile; tier distribution in analytics; dashboard Tiers page ✅

Roadmap to chaay-parity (loyalty-only): **P7 Tiers ✅** → P8 Rewards & Redemptions (catalog,
redeem points, QR coupons, reserve→confirm→cancel) → P9 Gamification (badges, streaks, challenges,
leaderboards) → P10 Referrals + Stamp cards + Birthday/scheduled → P11 Customer-facing app →
P12 richer dashboard + RFM personas + Kurdish. (Unified messaging hub intentionally out of scope.)

---

## Phase 6 status (done) — generic integration kit

The platform is **generic and multi-tenant**; every customer is an `Application`. P6 ships a
provider-agnostic integration kit, with Hareer kept as **one worked example** — **no changes were
made to any external/customer codebase** (e.g. the Hareer repo is untouched; its team imports the
emitter template themselves).

- Generic **onboarding CLI** (`integration/onboard.mjs`): platform bootstrap + per-tenant onboarding
  (child application + API key + loyalty/CVM settings + base-earn/welcome campaigns) ✅
- Generic **backfill CLI** (`integration/backfill.mjs`): replays history from a customer-provided
  **export file** (never a source DB), idempotent by business key ✅
- Provider-agnostic **emitter template** (`integration/emitter-template.ts`) + generic integration
  guide (`integration/README.md`) ✅
- Hareer example retained under `integration/hareer/` (contract, reference emitter, mock, samples) ✅

### Phase 6 note
- **Backfill lifecycle**: a backfilled member's `LastActivityAt` is the onboarding/processing time
  (provisioning initializes activity to creation time per §11.2), so historical members appear
  recently-active. `FirstActivityAt` is still moved to the historical date. Acceptable for onboarding.

---

## Phase 5 status (done)
- **EF Core migrations** replace EnsureCreated; applied at startup (+ idempotent balancing trigger) ✅
- **Durable PostgreSQL transport**: API publishes + listens, and `Loyalty.Worker` is a cross-process
  competing consumer (dedicated processor pool). Kafka still deferred ✅
- **Reconciliation**: wallet projections recomputed from ledger postings (credits − debits) with
  mismatch report + optional repair; profile PointsEarned checked against the ledger ✅
- **Rate limiting** per API key on /events (429) + request-body cap ✅
- **Health checks**: `/health` liveness, `/health/ready` with a PostgreSQL check ✅
- **Dead-letter ops**: list requests by status + controlled `/requests/{id}/replay` ✅
- **OpenTelemetry** traces + metrics (ASP.NET/HTTP/runtime; OTLP when configured) + scoped log
  enrichment (ApplicationId/AccountId/CorrelationId; secrets never logged) ✅

### Phase 5 remaining deltas
| Area | Spec | Phase 5 choice | When |
|---|---|---|---|
| Worker DB migration ordering | — | API runs migrations at startup; Worker assumes schema exists (start API first) | ok operationally |
| Profile rebuild | full event replay reconciliation | wallet + PointsEarned reconciled; full event-replay profile rebuild not yet | later |
| Load tests | included | not run here | later |

## Phase 4 status (done)
- Result events (PointsAwardedV1 / PointsReversedV1 / PointsAdjustedV1 / MemberLifecycleChangedV1)
  written to an `event_log` in the processing transaction (hand-rolled transactional outbox) ✅
- Notifications feed endpoint + dashboard page ✅
- Webhook subscriptions (CRUD) + background dispatcher with HMAC-SHA256 signing + bounded retry;
  signed delivery verified end-to-end against a real listener ✅
- Analytics overview endpoint (members, active campaigns/segments, points issued/reversed,
  active balance, lifecycle distribution) + dashboard Overview page ✅
- Frozen **CVM settings snapshot** on the ingestion request; lifecycle now evaluated from the
  snapshot in the worker (retry-consistent) ✅
- **RewardDecisionRecord** persisted (profile version, lifecycle, segment versions, outcome) for
  explainability ✅

### Phase 4 remaining deltas
| Area | Spec | Phase 4 choice | When |
|---|---|---|---|
| Webhook delivery | durable outbox + retry queues | hand-rolled event_log outbox + polling dispatcher (at-least-once, bounded attempts) | fine; move to Wolverine/Kafka transport in P5 |
| Segment-version binding in campaigns | versioned refs stored in CampaignVersion at activation | decision records the versions used; campaigns still evaluate against current active memberships | P5 |
| Consent/marketing policy checks | required before outbound marketing | not enforced (webhooks are system integration, not end-user marketing) | when notifications target end users |

## Phase 3 status (done)
- Cross-campaign stacking: one base × highest eligible multiplier + summed stackable bonuses ✅
  (reference case 149.50 ×1.5 +50 → 273; base counted once across campaigns)
- Member limits via CampaignUsage (CalendarDay/Month/AllTime), enforced + incremented ✅
- Global budget with atomic conditional reservation (`UPDATE … WHERE remaining >= n`) ✅
- Campaign validate + simulate; activation requires Validated ✅
- Refund reversal: compensating ledger Reverse of the original earn (idempotent) ✅
- Microsoft RulesEngine adapter behind the same `ILoyaltyRuleEvaluator` port, toggled by
  `LOYALTY_RULES_ENGINE=rulesengine` (default remains the deterministic operator evaluator);
  parity unit-tested ✅

### Phase 3 remaining deltas
| Area | Spec | Phase 3 choice | When |
|---|---|---|---|
| Budget attribution under stacking | budget charged on the campaign's issued points | per-campaign independent points for budget/usage; member award uses combined stacking | acceptable approximation; revisit if needed |
| CVM/segment-version snapshot | freeze on request / store in RewardDecision | still resolved live; RewardDecision not persisted as a row | P4 |
| Simulate | 202-style + full pipeline | synchronous single-campaign eligibility + computed effects | fine for admin preview |

---

## Phases 1–2 deltas

## Phase 2 status (done)
- ApplicationCvmSettings with nearest-ancestor inheritance ✅
- MemberValueProfile + projectors (Register/Login/Purchase/Refund/Referral, OccurredAt min/max) ✅
- Deterministic lifecycle evaluation (per-event) + daily inactivity sweep job + admin trigger ✅
- Rule-based segments (definition/version/membership) + activation + membership transitions ✅
- Campaign audience targeting (lifecycle + required/excluded segments), enforced in reward decision ✅
- All six event types registered with business keys; ManualAdjustment posts a signed Adjust ledger entry ✅

### Phase 2 remaining deltas
| Area | Spec | Phase 2 choice | When |
|---|---|---|---|
| Segment activation | 202 + background batch evaluation | synchronous activation (evaluates existing profiles inline) | fine at current scale; async batch when member bases are large |
| CVM snapshot | freeze effective CVM settings on the request | CVM resolved live at processing time | freeze alongside loyalty snapshot in P3 |
| Lifecycle sweep | all operational apps | apps with their own active CVM settings (inherited-only descendants covered on next event) | full inherited sweep later |
| Refund effect | compensating ledger reversal of the original earn | profile net-spend projection only (no auto points reversal) | reversal-by-original-tx in P3 |
| Segment versioning in campaigns | store versioned segment refs in CampaignVersion at activation | store segment ids; evaluate against current active memberships | versioned snapshot in P3 |
| Result events | MemberLifecycleChangedV1 / PointsAwardedV1 outbox | not emitted (no consumer yet) | P4 (webhooks/notifications) |

---

## Phase 1 deltas

| Area | Spec | Phase 1 choice | Why / when |
|---|---|---|---|
| Rules engine | Microsoft RulesEngine behind `ILoyaltyRuleEvaluator` | Deterministic `OperatorRuleEvaluator` behind the same port | Faster, test-friendly slice. RulesEngine adapter drops in with no caller changes (P3). |
| Worker host | Separate `Loyalty.Worker` process | API host self-processes via a **durable local queue** (Wolverine + Postgres) | Cross-process delivery needs the Postgres transport; wired in P5. `Loyalty.Worker` is scaffolded. |
| Schema creation | EF migrations | `EnsureCreated` + idempotent balancing-trigger SQL at startup | Keeps the slice simple; switch to migrations before prod. |
| Campaign stacking | base + stackable bonuses × highest eligible multiplier (cross-campaign) | per-campaign-effect computation (base × multiplier, fixed bonuses) + exclusivity by priority | Full stacking/limits/budgets land with the campaign engine (P3). Single-campaign reference case (§4 → 273) already passes. |
| CVM (lifecycle + segments) | included in MVP | implemented in P2 ✅ | see Phase 2 status above |
| Event types | Purchase, Refund, Register, Login, Referral, ManualAdjustment | all six implemented in P2 ✅ | — |
| Result events | `PointsAwardedV1` outbox + consumers | not emitted yet | P2/P4 (webhooks/notifications). |
| Deferred balance trigger | deferred constraint trigger | implemented (see `DbBootstrap`) ✅ | — |
| Idempotency (4 boundaries) | HTTP key, source-event, business-event, effect/ledger | all four implemented ✅ | — |
| Bilingual AR/EN | not in spec | added: localized API errors (`Accept-Language`), `NameAr` on Application/Campaign, RTL-ready dashboard | user requirement |

## Roadmap
- **P2** CVM (value profile, lifecycle job, segments, audience targeting) + remaining event types. ✅ DONE
- **P3** Campaign engine (stacking, member limits, global budget, validate/simulate) + RulesEngine adapter + refund reversal. ✅ DONE (CVM/segment-version snapshots moved to P4).
- **P4** Dashboard depth + analytics + CVM snapshot + result events + webhooks. ✅ DONE
- **P5** Reliability/ops: EF migrations, Postgres transport + dedicated worker, reconciliation, rate limits, health, dead-letter replay, OpenTelemetry. ✅ DONE (load tests + segment-version binding + full profile-rebuild deferred).
- **P6** Generic integration kit (onboarding + backfill CLIs, emitter template, guide); Hareer = one example; no external repo touched. ✅ DONE
