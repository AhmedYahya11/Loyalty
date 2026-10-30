# Deviations from the MVP spec (Phase 1 + Phase 2)

The authoritative design is `docs/design/MVP_IMPLEMENTATION_PLAN.md`. Phase 1 delivered the
**core pipeline vertical slice**; Phase 2 adds **CVM** (value profiles, lifecycle, segments),
**audience targeting**, and the **remaining event types**. Bilingual (AR/EN) throughout.
These are the intentional, documented deltas.

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
- **P3** Campaign engine (priority/stacking/exclusivity, member limits, global budget, validate/simulate) + RulesEngine adapter + refund reversal + CVM/segment-version snapshots.
- **P4** Dashboard depth + analytics.
- **P5** Reliability/ops: Postgres transport (dedicated worker), retries/dead-letter ops, reconciliation, rate limits, OpenTelemetry, migrations, load tests.
- **P6** Hareer live integration (emit from the real repo, map tenants→applications, backfill).
